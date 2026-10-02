using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnapContext;

/// <summary>
/// Week 1 PoC: 전역 핫키(Ctrl+Alt+S) → 영역 선택 → GDI BitBlt 캡처 → 클립보드 즉시 반영.
/// 규칙 A-1: 캡처 트리거 후 클립보드 반영까지 어떤 필수 입력도 끼어들지 않는다
/// (영역 드래그 선택 자체는 캡처 동작 그 자체이지, "선택적 설명 입력" 단계가 아님).
/// </summary>
public partial class App : Application
{
    private const uint VkS = 0x53;

    private HotkeyManager? _hotkeyManager;
    private Window? _messageWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 트레이 상주(Week 4 예정) 전제로, 창이 하나도 안 보여도 앱이 종료되지 않게 함
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
        _hotkeyManager.HotkeyPressed += OnCaptureHotkeyPressed;

        bool registered = _hotkeyManager.TryRegister(ModifierKeys.Control | ModifierKeys.Alt, VkS);
        if (!registered)
        {
            // 규칙 8-1/C-12: 등록 실패는 사용자에게 명확히 안내한다.
            // 재설정 UI/대체 키 자동 제안은 이후 단계(Week 2+) 구현 예정 - 지금은 PoC 단계.
            MessageBox.Show(
                "기본 단축키(Ctrl+Alt+S) 등록에 실패했습니다. 다른 프로그램과 충돌 중일 수 있습니다.\n" +
                "(정식 재설정 UI는 이후 단계에서 구현 예정 — 현재는 Week 1 PoC)",
                "SnapContext - 핫키 등록 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCaptureHotkeyPressed()
    {
        var overlay = new RegionSelectionWindow();
        var result = overlay.ShowDialog();

        if (result != true || overlay.SelectedRegion is not { Width: > 0, Height: > 0 } region)
        {
            return; // Esc 취소 또는 너무 작은 선택
        }

        using var bitmap = ScreenCapture.CaptureRegion(region);
        var bitmapSource = ScreenCapture.ToBitmapSource(bitmap);

        // 규칙 A-1: 다른 입력을 기다리지 않고 즉시 클립보드 반영
        Clipboard.SetImage(bitmapSource);

        ShowCaptureToast();
    }

    /// <summary>
    /// 규칙 A-2/A-3의 축소판 미리보기: 포커스를 뺏지 않고(ShowActivated=false),
    /// 사용자 행동 없이도 자동으로 소멸하는 알림. 설명 입력 UI는 Week 2에서 구현.
    /// </summary>
    private void ShowCaptureToast()
    {
        var toast = new Window
        {
            Width = 340,
            Height = 56,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)),
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "✓ 캡처 완료 — 클립보드에 복사됨 (바로 Ctrl+V 가능)",
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        toast.Left = SystemParameters.WorkArea.Right - toast.Width - 20;
        toast.Top = SystemParameters.WorkArea.Bottom - toast.Height - 20;
        toast.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            toast.Close();
        };
        timer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        base.OnExit(e);
    }
}
