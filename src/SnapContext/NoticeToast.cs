using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnapContext;

/// <summary>
/// 단축키 결과 같은 짧은 안내를 우하단에 띄운다. 포커스를 뺏지 않고(규칙 A-2) 몇 초 뒤 저절로 사라진다(규칙 A-3).
/// 사용자가 ChatGPT 입력창에서 단축키를 눌렀을 때, 입력창의 포커스가 그대로여야 곧바로 Ctrl+V를 할 수 있다.
/// 새 안내를 띄우면 이전 안내는 닫는다.
/// </summary>
public sealed class NoticeToast : Window
{
    private const double ScreenMargin = 8;

    private static NoticeToast? s_current;

    private readonly DispatcherTimer _timer;

    public static void Show(string message, bool warning = false, int durationMs = 2400)
    {
        s_current?.Close();
        var notice = new NoticeToast(message, warning, durationMs);
        s_current = notice;
        ((Window)notice).Show();
    }

    private NoticeToast(string message, bool warning, int durationMs)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI, Malgun Gothic");

        Content = new Border
        {
            Margin = new Thickness(ScreenMargin),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x21, 0x24)),
            Child = new TextBlock
            {
                Text = message,
                FontSize = 13,
                MaxWidth = 400,
                TextWrapping = TextWrapping.Wrap,
                Foreground = warning
                    ? new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))
                    : Brushes.White,
            },
        };

        SourceInitialized += (_, _) => NoActivateStyle.Apply(new WindowInteropHelper(this).Handle, true);
        SizeChanged += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth;
            Top = workArea.Bottom - ActualHeight;
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _timer.Tick += (_, _) => Close();
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            if (ReferenceEquals(s_current, this))
            {
                s_current = null;
            }
        };
    }
}
