using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace SnapContext;

public sealed record AiResult(bool Ok, string Text, string? Error);

/// <summary>
/// 버전 A/B에서 쓰는 Claude API 호출. 공식 Anthropic C# SDK를 사용한다.
/// - API 키는 환경 변수 ANTHROPIC_API_KEY에서만 읽고, 파일에 저장하거나 로그로 남기지 않는다.
/// - 모델은 기본 claude-opus-5-5이며 SNAPCONTEXT_AI_MODEL로 바꿀 수 있다(속도/비용 비교용).
/// - 테스트용으로 SNAPCONTEXT_AI_BASE_URL을 줄 수 있지만 localhost 주소만 받아들인다.
/// </summary>
public sealed class AiClient
{
    public const string DefaultModel = "claude-opus-5-5";

    private const int MaxImageEdge = 1568;
    private const long MaxImageBytes = 4_000_000;
    private const int MaxSuggestionChars = 400;

    private const string TextSystemPrompt =
        "당신은 스크린샷과 함께 AI 챗봇에 붙여 넣을 설명 문구를 다듬는 도우미입니다. " +
        "사용자가 적은 짧은 메모를 받아, 의도를 바꾸거나 메모에 없는 사실을 지어내지 않고 AI가 이해하기 쉬운 명확한 요청문으로 다시 쓰세요. " +
        "1~3문장으로, 메모와 같은 언어로 쓰고, 설명·따옴표·머리말 없이 완성된 문장만 출력하세요.";

    private const string ImageSystemPrompt =
        "당신은 스크린샷과 함께 AI 챗봇에 붙여 넣을 설명 문구를 작성하는 도우미입니다. " +
        "첨부된 스크린샷에서 실제로 보이는 내용(어떤 화면인지, 보이는 오류 메시지나 핵심 텍스트)을 바탕으로 AI가 이 화면을 이해하고 도울 수 있게 1~4문장으로 작성하세요. " +
        "사용자 메모가 있으면 그 의도를 최우선으로 반영하고, 메모가 없으면 이 화면에서 무엇을 도와 달라는 자연스러운 요청문으로 마무리하세요. " +
        "이미지에 보이지 않는 사실을 추측해 쓰지 마세요. " +
        "비밀번호, 토큰, 카드번호, 주민등록번호처럼 민감해 보이는 값은 절대 옮겨 적지 말고 '(민감 정보)'로 표기하세요. " +
        "메모와 같은 언어(메모가 없으면 한국어)로, 설명·따옴표·머리말 없이 완성된 문장만 출력하세요.";

    private static readonly AiResult NotConfiguredResult =
        new(false, "", "ANTHROPIC_API_KEY 환경 변수가 설정되지 않아 AI를 쓸 수 없습니다.");

    private readonly AnthropicClient? _client;
    private readonly string _model;

    public AiClient(string? apiKey, string? baseUrl = null, string? model = null, TimeSpan? timeout = null)
    {
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client = new AnthropicClient
            {
                ApiKey = apiKey.Trim(),
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
                MaxRetries = 0,
            };

            if (IsLocalhost(baseUrl))
            {
                _client = new AnthropicClient
                {
                    ApiKey = apiKey.Trim(),
                    BaseUrl = baseUrl!,
                    Timeout = timeout ?? TimeSpan.FromSeconds(30),
                    MaxRetries = 0,
                };
            }
        }
    }

    public bool IsConfigured => _client is not null;

    public string Model => _model;

    public static AiClient FromEnvironment() => new(
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
        Environment.GetEnvironmentVariable("SNAPCONTEXT_AI_BASE_URL"),
        Environment.GetEnvironmentVariable("SNAPCONTEXT_AI_MODEL"));

    /// <summary>버전 A: 메모 글만 보내 다듬은 문장을 받는다(이미지는 보내지 않는다).</summary>
    public Task<AiResult> RewriteMemoAsync(string memo, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return Task.FromResult(NotConfiguredResult);
        }

        var message = new MessageParam { Role = Role.User, Content = $"메모: {memo}" };
        return SendAsync(TextSystemPrompt, message, cancellationToken);
    }

    /// <summary>
    /// 버전 B: 스크린샷(축소본)과 메모를 보내 설명을 받는다.
    /// 이미지는 호출 즉시(첫 await 이전) 읽어 인코딩하므로, 호출한 직후 호출자가 비트맵을 해제해도 안전하다.
    /// </summary>
    public Task<AiResult> DescribeImageAsync(System.Drawing.Bitmap image, string? memo, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return Task.FromResult(NotConfiguredResult);
        }

        var (bytes, mediaType) = EncodeForUpload(image);
        var content = new List<ContentBlockParam>
        {
            new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data = Convert.ToBase64String(bytes),
                    MediaType = mediaType,
                },
            },
            new TextBlockParam { Text = string.IsNullOrWhiteSpace(memo) ? "메모: (없음)" : $"메모: {memo}" },
        };

        var message = new MessageParam { Role = Role.User, Content = content };
        return SendAsync(ImageSystemPrompt, message, cancellationToken);
    }

    private async Task<AiResult> SendAsync(string systemPrompt, MessageParam message, CancellationToken cancellationToken)
    {
        var request = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 1024,
            System = systemPrompt,
            Messages = [message],
        };

        if (SupportsEffort(_model))
        {
            request = request with { OutputConfig = new OutputConfig { Effort = Effort.Low } };
        }

        try
        {
            var response = await _client!.Messages.Create(request, cancellationToken);

            if (response.StopReason == "refusal")
            {
                return new AiResult(false, "", "AI가 이 요청에 대한 제안을 거절했습니다.");
            }

            var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
            if (text.Length == 0)
            {
                return new AiResult(false, "", "AI가 빈 응답을 돌려보냈습니다.");
            }

            return new AiResult(true, Clamp(text), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new AiResult(false, "", "AI 응답 시간이 초과되었습니다.");
        }
        catch (AnthropicUnauthorizedException)
        {
            return new AiResult(false, "", "API 키가 올바르지 않습니다(ANTHROPIC_API_KEY 확인).");
        }
        catch (AnthropicRateLimitException)
        {
            return new AiResult(false, "", "요청이 너무 많거나 사용 한도를 넘었습니다. 잠시 후 다시 시도해 주세요.");
        }
        catch (AnthropicApiException)
        {
            return new AiResult(false, "", "AI 서비스에서 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.");
        }
        catch (Exception ex) when (ex is HttpRequestException or AnthropicIOException)
        {
            return new AiResult(false, "", "AI 서비스에 연결하지 못했습니다. 네트워크를 확인해 주세요.");
        }
    }

    private static string Clamp(string text)
    {
        text = text.Trim().Trim('"', '“', '”').Trim();
        return text.Length <= MaxSuggestionChars ? text : text[..MaxSuggestionChars].TrimEnd() + "…";
    }

    private static bool SupportsEffort(string model) =>
        model.Contains("opus-5", StringComparison.OrdinalIgnoreCase)
        || model.Contains("sonnet-5", StringComparison.OrdinalIgnoreCase)
        || model.Contains("fable", StringComparison.OrdinalIgnoreCase)
        || model.Contains("mythos", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalhost(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    /// <summary>긴 변을 1568px 이하로 줄여 PNG로 인코딩하고, 4MB를 넘으면 JPEG로 낮춘다(전송량·비용·노출 최소화).</summary>
    private static (byte[] Bytes, string MediaType) EncodeForUpload(System.Drawing.Bitmap source)
    {
        double scale = Math.Min(1.0, (double)MaxImageEdge / Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = new System.Drawing.Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(
                source,
                new System.Drawing.Rectangle(0, 0, width, height),
                0, 0, source.Width, source.Height,
                System.Drawing.GraphicsUnit.Pixel);
        }

        using var png = new MemoryStream();
        resized.Save(png, ImageFormat.Png);
        if (png.Length <= MaxImageBytes)
        {
            return (png.ToArray(), "image/png");
        }

        var jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        using var jpeg = new MemoryStream();
        resized.Save(jpeg, jpegCodec, parameters);
        return (jpeg.ToArray(), "image/jpeg");
    }
}
