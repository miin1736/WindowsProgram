using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnapContext;

/// <summary>OCR이 돌려준 글자 조각과 위치(이미지 좌표).</summary>
public readonly record struct OcrSegment(string Text, double Left, double Top, double Right, double Bottom);

/// <summary>Ok는 인식이 실제로 실행되었다는 뜻이며 글자가 없으면 Text가 빈 문자열이다. 실행하지 못했으면 Ok=false와 사유.</summary>
public sealed record OcrOutcome(bool Ok, string Text, string? Error);

/// <summary>
/// OCR 조각을 읽는 순서(위→아래, 같은 줄은 왼쪽→오른쪽)로 이어 붙인다.
/// OcrResult.Text는 여러 줄이 섞여 순서가 뒤엉키므로(PROGRESS.md "로컬 OCR 시험 결과") 위치로 직접 정렬한다.
/// </summary>
public static class OcrTextAssembler
{
    public static string Assemble(IEnumerable<OcrSegment> segments)
    {
        var ordered = segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .OrderBy(s => (s.Top + s.Bottom) / 2)
            .ToList();
        if (ordered.Count == 0)
        {
            return "";
        }

        var rows = new List<List<OcrSegment>>();
        foreach (var segment in ordered)
        {
            double centerY = (segment.Top + segment.Bottom) / 2;
            double height = segment.Bottom - segment.Top;

            if (rows.Count > 0)
            {
                var row = rows[^1];
                double rowCenter = row.Average(s => (s.Top + s.Bottom) / 2);
                double rowHeight = row.Average(s => s.Bottom - s.Top);
                if (Math.Abs(centerY - rowCenter) <= 0.5 * Math.Max(height, rowHeight))
                {
                    row.Add(segment);
                    continue;
                }
            }

            rows.Add(new List<OcrSegment> { segment });
        }

        return string.Join("\n", rows.Select(row =>
            string.Join(" ", row.OrderBy(s => s.Left).Select(s => s.Text.Trim()))));
    }

    /// <summary>설명 입력창에 채울 한 줄 초안을 만든다(줄바꿈 → 공백, 연속 공백 정리, 길면 …로 자름).</summary>
    public static string ToDraft(string text, int maxChars = 200)
    {
        var collapsed = string.Join(" ", text.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxChars ? collapsed : collapsed[..(maxChars - 1)].TrimEnd() + "…";
    }
}

/// <summary>
/// Windows 내장 OCR(Windows.Media.Ocr)로 캡처 이미지의 글자를 읽는다. 모든 처리는 이 PC 안에서만 일어난다.
/// - 설치 파일 없이 실행하는 exe에서도 동작함을 확인했다(PROGRESS.md "로컬 OCR 시험 결과"). 다만 마이크로소프트 문서상 비지원 사용이다.
/// - 작은 글자(11~13px)는 2배 확대하면 정확도가 0.69 → 0.92로 오른다. 큰 이미지는 글자도 크므로 확대하지 않는다.
/// - 인식 언어는 사용자 프로필 언어를 따르며, OCR 언어가 설치되어 있지 않으면 사유를 돌려준다(예외로 죽지 않는다).
/// 호출자는 캡처 직후 클립보드 반영과 토스트 표시를 끝낸 뒤 이 메서드를 부른다(규칙 A-1: OCR이 그 흐름을 늦추지 않는다).
/// </summary>
public sealed class LocalOcr
{
    public const string MissingLanguageMessage =
        "이 PC에는 글자 인식(OCR) 언어가 설치되어 있지 않습니다. Windows 설정 > 시간 및 언어 > 언어 및 지역에서 사용하는 언어의 옵션을 열어 OCR(글자 인식)을 추가하세요.";

    private const int MinEdge = 16;
    private const long UpscaleMaxPixels = 3_000_000;

    private readonly Func<OcrEngine?> _engineFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OcrEngine? _engine;
    private bool _engineTried;
    private string? _unavailableReason;

    /// <param name="engineFactory">테스트에서 엔진 생성을 바꿔 끼우기 위한 훅. 기본은 사용자 프로필 언어.</param>
    public LocalOcr(Func<OcrEngine?>? engineFactory = null)
    {
        _engineFactory = engineFactory ?? CreateDefaultEngine;
    }

    /// <summary>
    /// 이미지를 복사한 뒤 백그라운드에서 인식한다. 이 메서드가 반환되기 전에 복사를 끝내므로,
    /// 호출한 직후 호출자가 원본 비트맵을 해제해도 안전하다. 취소하면 OperationCanceledException이 난다.
    /// </summary>
    public Task<OcrOutcome> RecognizeAsync(Bitmap source, CancellationToken cancellationToken)
    {
        var copy = (Bitmap)source.Clone();
        return Task.Run(async () =>
        {
            try
            {
                return await RecognizeCoreAsync(copy, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                copy.Dispose();
            }
        }, CancellationToken.None);
    }

    private async Task<OcrOutcome> RecognizeCoreAsync(Bitmap image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (image.Width < MinEdge || image.Height < MinEdge)
        {
            return new OcrOutcome(false, "", "선택한 영역이 너무 작아 글자를 인식하지 않았습니다.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var engine = GetEngine();
            if (engine is null)
            {
                return new OcrOutcome(false, "", _unavailableReason ?? MissingLanguageMessage);
            }

            try
            {
                using var prepared = Prepare(image, (int)OcrEngine.MaxImageDimension);
                using var png = new MemoryStream();
                prepared.Save(png, ImageFormat.Png);
                png.Position = 0;

                using var stream = png.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
                using var bitmap = await decoder
                    .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                    .AsTask(ct).ConfigureAwait(false);
                var result = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);

                var segments = result.Lines.Select(line =>
                {
                    var words = line.Words;
                    if (words.Count == 0)
                    {
                        return new OcrSegment(line.Text, 0, 0, 0, 0);
                    }

                    return new OcrSegment(
                        line.Text,
                        words.Min(w => w.BoundingRect.X),
                        words.Min(w => w.BoundingRect.Y),
                        words.Max(w => w.BoundingRect.X + w.BoundingRect.Width),
                        words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height));
                });

                return new OcrOutcome(true, OcrTextAssembler.Assemble(segments), null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return new OcrOutcome(false, "", "글자 인식 중 오류가 발생했습니다.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private OcrEngine? GetEngine()
    {
        if (_engineTried)
        {
            return _engine;
        }

        _engineTried = true;
        try
        {
            _engine = _engineFactory();
            if (_engine is null)
            {
                _unavailableReason = MissingLanguageMessage;
            }
        }
        catch (Exception)
        {
            _unavailableReason = "이 Windows 버전에서는 글자 인식을 사용할 수 없습니다.";
        }
        return _engine;
    }

    private static OcrEngine? CreateDefaultEngine()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is not null)
        {
            return engine;
        }

        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
        {
            engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is not null)
            {
                return engine;
            }
        }
        return null;
    }

    /// <summary>작은 이미지는 2배 확대(작은 글자 인식률↑), 엔진 한계를 넘는 이미지는 줄인다.</summary>
    private static Bitmap Prepare(Bitmap source, int maxDimension)
    {
        double scale = 1.0;
        long pixels = (long)source.Width * source.Height;
        if (pixels <= UpscaleMaxPixels && Math.Max(source.Width, source.Height) * 2 <= maxDimension)
        {
            scale = 2.0;
        }
        else if (Math.Max(source.Width, source.Height) > maxDimension)
        {
            scale = (double)maxDimension / Math.Max(source.Width, source.Height);
        }

        if (scale == 1.0)
        {
            return (Bitmap)source.Clone();
        }

        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        return scaled;
    }
}
