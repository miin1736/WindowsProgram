using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Bitmap = System.Drawing.Bitmap;

namespace SnapContext;

/// <summary>
/// 전역 핫키(Ctrl+Alt+S) → 영역 선택 → GDI BitBlt 캡처 → 클립보드 즉시 반영 → 비모달 설명 토스트.
/// 규칙 A-1: 클립보드 반영까지 어떤 필수 입력도 끼어들지 않는다(영역 드래그 선택은 캡처 동작 그 자체).
/// 설명은 캡처 전에 정해 둔 프리셋(PresetStore)으로 전달한다: 선택 화면에서 숫자키로 고르거나 기본 프리셋이 적용되며,
/// 캡처하는 순간 그 설명을 캡션으로 번인한 이미지 한 장이 클립보드에 한 번에 올라간다.
/// 알림창에서는 프리셋을 바꾸거나 직접 입력해 설명을 고칠 수 있다(선택 사항).
///
/// 실행 인자 `--mode A|B`로 AI 설명 보강 실험 버전을 고른다(생략하면 AI 없음).
/// A/B는 환경 변수 ANTHROPIC_API_KEY가 있어야 하고, 처음 쓸 때 외부 전송 동의를 받는다.
/// </summary>
public partial class App : Application
{
    private readonly RecentDescriptionStore _recents = new();
    private readonly AiConsentStore _consent = new();
    private readonly PresetStore _presetStore = new();

    private EnhanceMode _mode = EnhanceMode.Plain;
    private AiClient? _ai;
    private HotkeyManager? _hotkeyManager;
    private Window? _messageWindow;
    private CaptureToastWindow? _toast;
    private TrayIcon? _tray;
    private PresetEditorWindow? _presetEditor;
    private bool _captureInProgress;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 트레이 상주 전제로, 창이 하나도 안 보여도 앱이 종료되지 않게 함(종료는 트레이 메뉴의 종료)
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

        // 트레이 아이콘: 앱이 켜져 있다는 표시이자 프리셋 편집 창과 종료의 진입점
        _tray = new TrayIcon(
            AppHotkeys.CaptureText,
            () => Dispatcher.BeginInvoke(new Action(OnCaptureHotkeyPressed)),
            () => Dispatcher.BeginInvoke(new Action(ShowPresetEditor)),
            () => Dispatcher.BeginInvoke(new Action(Shutdown)));
    }

    /// <summary>프리셋 편집 창은 하나만 연다. 이미 열려 있으면 앞으로 가져온다.</summary>
    private void ShowPresetEditor()
    {
        if (_presetEditor is not null)
        {
            _presetEditor.Activate();
            return;
        }

        var editor = new PresetEditorWindow(_presetStore);
        editor.Closed += (_, _) => _presetEditor = null;
        _presetEditor = editor;
        editor.Show();
        editor.Activate();
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

            // 파일이 없거나 깨져도 예외 없이 기본 프리셋으로 복구되므로 캡처 흐름을 막지 않는다.
            var presets = _presetStore.Load();

            var overlay = new RegionSelectionWindow(presets);
            var result = overlay.ShowDialog();

            if (result != true || overlay.SelectedRegion is not { Width: > 0, Height: > 0 } region)
            {
                return; // Esc 취소 또는 너무 작은 선택
            }

            var preset = overlay.SelectedPreset;
            var bitmap = ScreenCapture.CaptureRegion(region);

            // 규칙 A-1: 다른 입력을 기다리지 않고, 프리셋 설명을 합성한 최종 이미지를 클립보드에 한 번에 반영
            var (copied, applied) = CaptureComposer.Copy(bitmap, preset);

            ShowToast(bitmap, copied, presets, applied);
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private void ShowToast(Bitmap original, bool copied, PresetSet presets, Preset? applied)
    {
        var toast = new CaptureToastWindow(
            _hotkeyManager!,
            copied ? _recents.Load() : Array.Empty<string>(),
            presets.Presets,
            applied,
            copied,
            description => ApplyDescription(original, description, presets),
            _mode,
            BuildSuggestionRequest(original));

        // 원본 비트맵은 토스트가 사라질 때까지(설명 변경, AI 제안 가능 시간 동안) 보관한다.
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

    private bool ApplyDescription(Bitmap original, string description, PresetSet presets)
    {
        try
        {
            using var composed = CaptionRenderer.Compose(original, description);
            if (!ClipboardHelper.TrySetImage(ScreenCapture.ToBitmapSource(composed)))
            {
                return false;
            }

            // 프리셋 칩으로 바꾼 설명은 칩으로 다시 고를 수 있으므로 "최근 설명"에 중복해 쌓지 않는다.
            if (!presets.Presets.Any(p => description.StartsWith(p.Caption, StringComparison.Ordinal)))
            {
                _recents.Add(description);
            }

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
        _tray?.Dispose();
        _hotkeyManager?.Dispose();
        base.OnExit(e);
    }
}
