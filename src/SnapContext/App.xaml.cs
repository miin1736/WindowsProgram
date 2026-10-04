using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Bitmap = System.Drawing.Bitmap;

namespace SnapContext;

/// <summary>
/// 전역 핫키(Ctrl+Alt+S) → 영역 선택 → GDI BitBlt 캡처 → 클립보드 즉시 반영 → 비모달 설명 토스트.
/// 규칙 A-1: 클립보드 반영까지 어떤 필수 입력도 끼어들지 않는다(영역 드래그 선택은 캡처 동작 그 자체).
/// 설명은 선택사항이며, 입력하면 캡션으로 번인한 이미지로 클립보드를 갱신한다.
///
/// 실행 인자 `--mode C|A|B`로 설명 보강 버전을 고른다(생략하면 입력한 글 그대로).
/// A/B는 환경 변수 ANTHROPIC_API_KEY가 있어야 하고, 처음 쓸 때 외부 전송 동의를 받는다.
/// </summary>
public partial class App : Application
{
    private static readonly string[] DefaultTags = { "버그", "UI 검토", "에러 로그" };

    private readonly RecentDescriptionStore _recents = new();
    private readonly LastDescription _lastDescription = new();
    private readonly AiConsentStore _consent = new();
    private readonly LocalOcr _ocr = new();

    private EnhanceMode _mode = EnhanceMode.Plain;
    private AiClient? _ai;
    private HotkeyManager? _hotkeyManager;
    private Window? _messageWindow;
    private CaptureToastWindow? _toast;
    private bool _captureInProgress;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 트레이 상주(Week 4 예정) 전제로, 창이 하나도 안 보여도 앱이 종료되지 않게 함
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mode = EnhanceModes.FromArgs(e.Args);
        if (EnhanceModes.UsesAi(_mode))
        {
            _ai = AiClient.FromEnvironment();
        }

        // 화면에 절대 보이지 않는 메시지 전용 윈도우 - 전역 핫키(WM_HOTKEY) 수신 목적
        _messageWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden,
        };

        _hotkeyManager = new HotkeyManager(_messageWindow);

        // WM_HOTKEY 처리 안에서 모달 오버레이를 열지 않도록 디스패처로 넘긴다.
        int id = _hotkeyManager.TryRegister(
            ModifierKeys.Control | ModifierKeys.Alt,
            AppHotkeys.VkCapture,
            () => Dispatcher.BeginInvoke(new Action(OnCaptureHotkeyPressed)));

        if (id == 0)
        {
            // 규칙 8-1/C-12: 등록 실패는 사용자에게 명확히 안내한다.
            // 재설정 UI/대체 키 자동 제안은 이후 단계 구현 예정.
            MessageBox.Show(
                "기본 단축키(Ctrl+Alt+S) 등록에 실패했습니다. 다른 프로그램(또는 이미 실행 중인 SnapContext)과 충돌 중일 수 있습니다.\n" +
                "(정식 재설정 UI는 이후 단계에서 구현 예정)",
                "SnapContext - 핫키 등록 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // 상시 켜 두는 두 번째 핫키: 알림창이 사라진 뒤에도 설명을 글자로 복사할 수 있어야 한다.
        int copyId = _hotkeyManager.TryRegister(
            ModifierKeys.Control | ModifierKeys.Alt,
            AppHotkeys.VkCopyDescription,
            () => Dispatcher.BeginInvoke(new Action(OnCopyDescriptionHotkeyPressed)));

        if (copyId == 0)
        {
            // 규칙 C-12: 등록 실패는 보이게 알린다(재설정 UI는 Week 4).
            NoticeToast.Show(
                $"설명 복사 단축키({AppHotkeys.CopyDescriptionText}) 등록에 실패했습니다. 다른 프로그램과 충돌 중일 수 있습니다.",
                warning: true,
                durationMs: 6000);
        }
    }

    /// <summary>
    /// 순차 붙여넣기: 이미지를 붙여넣은 뒤 이 단축키로 설명을 글자로 복사해 이어서 붙여넣는다.
    /// 결과는 포커스를 뺏지 않는 안내로 알려서, 사용자가 대화창에서 곧바로 Ctrl+V를 누를 수 있게 한다.
    /// </summary>
    private void OnCopyDescriptionHotkeyPressed()
    {
        if (_captureInProgress)
        {
            return;
        }

        switch (_lastDescription.TryCopy(text => ClipboardHelper.TrySetText(text)))
        {
            case DescriptionCopyResult.Copied:
                var preview = _lastDescription.Text!.Length <= 60 ? _lastDescription.Text : _lastDescription.Text[..60] + "…";
                NoticeToast.Show($"✓ 설명을 복사했습니다 — 붙여넣을 곳에서 Ctrl+V\n{preview}");
                break;
            case DescriptionCopyResult.NoDescription:
                NoticeToast.Show("복사할 설명이 없습니다. 캡처한 뒤 알림창에서 설명을 적용하세요.", warning: true);
                break;
            default:
                NoticeToast.Show("⚠ 설명을 복사하지 못했습니다. 잠시 후 다시 시도해 주세요.", warning: true);
                break;
        }
    }

    private void OnCaptureHotkeyPressed()
    {
        if (_captureInProgress)
        {
            return;
        }

        _captureInProgress = true;
        try
        {
            CloseToast();

            var overlay = new RegionSelectionWindow();
            var result = overlay.ShowDialog();

            if (result != true || overlay.SelectedRegion is not { Width: > 0, Height: > 0 } region)
            {
                return; // Esc 취소 또는 너무 작은 선택
            }

            var bitmap = ScreenCapture.CaptureRegion(region);

            // 이전 캡처의 설명이 새 이미지에 잘못 복사되지 않도록 지운다.
            _lastDescription.Clear();

            // 규칙 A-1: 다른 입력을 기다리지 않고 즉시 클립보드 반영
            bool copied = ClipboardHelper.TrySetImage(ScreenCapture.ToBitmapSource(bitmap));

            ShowToast(bitmap, copied);
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private void ShowToast(Bitmap original, bool copied)
    {
        var toast = new CaptureToastWindow(
            _hotkeyManager!,
            copied ? _recents.Load() : Array.Empty<string>(),
            DefaultTags,
            copied,
            description => ApplyDescription(original, description),
            _mode,
            BuildSuggestionRequest(original),
            // 글자 인식은 컴퓨터 안에서만 처리된다. LocalOcr가 비트맵을 먼저 복사하므로 토스트가 닫혀 원본이 해제되어도 안전하다.
            ct => _ocr.RecognizeAsync(original, ct),
            text => ClipboardHelper.TrySetText(text));

        // 원본 비트맵은 토스트가 사라질 때까지(설명 반영·AI 제안 가능 시간 동안) 보관한다.
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_toast, toast))
            {
                _toast = null;
            }
            original.Dispose();
        };

        _toast = toast;
        toast.Show();
    }

    private Func<string?, CancellationToken, Task<AiResult>>? BuildSuggestionRequest(Bitmap original) => _mode switch
    {
        EnhanceMode.TextRewrite => (memo, ct) =>
            RequestAiAsync("text", () => _ai!.RewriteMemoAsync(memo ?? "", ct)),
        EnhanceMode.ImageDescribe => (memo, ct) =>
            RequestAiAsync("image", () => _ai!.DescribeImageAsync(original, memo, ct)),
        _ => null,
    };

    private Task<AiResult> RequestAiAsync(string kind, Func<Task<AiResult>> call)
    {
        if (_ai is null || !_ai.IsConfigured)
        {
            return Task.FromResult(new AiResult(false, "", "ANTHROPIC_API_KEY 환경 변수가 설정되지 않아 AI를 쓸 수 없습니다."));
        }

        if (!EnsureConsent(kind))
        {
            return Task.FromResult(new AiResult(false, "", "외부 전송에 동의하지 않아 AI를 쓰지 않았습니다."));
        }

        return call();
    }

    /// <summary>처음 쓸 때 한 번, 어떤 데이터가 어디로 나가는지 알리고 동의를 받는다(동의는 종류별로 저장).</summary>
    private bool EnsureConsent(string kind)
    {
        if (_consent.Has(kind))
        {
            return true;
        }

        string what = kind == "image"
            ? "캡처한 스크린샷 이미지와 입력한 메모를"
            : "입력한 메모 글을(이미지는 제외)";

        var answer = MessageBox.Show(
            $"이 기능은 {what} Anthropic(Claude) API 서버로 전송합니다.\n\n" +
            "화면에 비밀번호, 개인정보, 회사 기밀이 보이면 사용하지 마세요.\n" +
            "전송은 이 기능을 실행하는 순간에만 일어나고, 동의는 이 PC에 저장됩니다.\n\n" +
            "전송에 동의하시겠습니까?",
            "SnapContext - AI 전송 동의",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        _consent.Grant(kind);
        return true;
    }

    private bool ApplyDescription(Bitmap original, string description)
    {
        try
        {
            using var composed = CaptionRenderer.Compose(original, description);
            if (!ClipboardHelper.TrySetImage(ScreenCapture.ToBitmapSource(composed)))
            {
                return false;
            }

            _recents.Add(description);
            _lastDescription.Set(description);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"캡션 반영 실패: {ex}");
            return false;
        }
    }

    private void CloseToast()
    {
        var toast = _toast;
        _toast = null;
        toast?.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CloseToast();
        _hotkeyManager?.Dispose();
        base.OnExit(e);
    }
}
