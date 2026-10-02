using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnapContext;

/// <summary>
/// 캡처 직후 우하단에 뜨는 알림 + 선택적 설명 입력 UI.
/// 이 창이 뜨는 시점에 클립보드에는 이미 이미지가 들어 있다(규칙 A-1).
///
/// 포커스(규칙 A-2): WS_EX_NOACTIVATE로 뜨므로 칩 클릭·표시 중에도 다른 앱의 포커스를 뺏지 않는다.
/// 사용자가 입력창을 직접 클릭했을 때만 활성화되어 타이핑할 수 있다.
/// 단축키: 맨 숫자키·Esc를 전역으로 가로채면 다른 앱에 입력하는 키를 삼키므로,
/// 최근 설명 재사용은 표시되는 동안에만 등록되는 Ctrl+Alt+1~5를 쓴다. Esc는 토스트가 활성화된 상태에서만 동작한다.
/// 자동 소멸(규칙 A-3): 마우스를 올려두거나 입력 중이면 카운트다운을 멈춘다.
/// </summary>
public partial class CaptureToastWindow : Window
{
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private const uint VkOne = 0x31;
    private const int DisplayMs = 4000;
    private const int GraceMs = 1500;
    private const int TickMs = 100;
    private const int AppliedMs = 1200;
    private const int FailedMs = 3000;
    private const double ScreenMargin = 8;

    private readonly Func<string, bool> _applyDescription;
    private readonly List<int> _hotkeyIds = new();
    private readonly HotkeyManager _hotkeys;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(TickMs) };

    private int _remainingMs = DisplayMs;
    private bool _noActivate = true;
    private bool _finished;
    private bool _resultShown;

    /// <param name="applyDescription">설명을 이미지에 반영하고 클립보드를 갱신한다. 성공 여부를 반환한다.</param>
    public CaptureToastWindow(
        HotkeyManager hotkeys,
        IReadOnlyList<string> recents,
        IReadOnlyList<string> tags,
        bool copied,
        Func<string, bool> applyDescription)
    {
        InitializeComponent();

        _hotkeys = hotkeys;
        _applyDescription = applyDescription;

        if (!copied)
        {
            HeaderText.Text = "⚠ 클립보드에 복사하지 못했습니다. 다시 캡처해 주세요.";
            ContentPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeaderText.Text = "✓ 캡처 완료 — 클립보드에 복사됨";
            BuildChips(recents, tags);
        }

        _timer.Tick += OnTick;
        _timer.Start();
        Closed += (_, _) => Cleanup();
    }

    private bool IsInteracting => IsMouseOver || InputBox.IsKeyboardFocusWithin;

    private void BuildChips(IReadOnlyList<string> recents, IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            var captured = tag;
            TagPanel.Children.Add(CreateChip(new TextBlock { Text = tag }, () => ApplyTag(captured), null));
        }

        int count = Math.Min(recents.Count, RecentDescriptionStore.MaxItems);
        for (int i = 0; i < count; i++)
        {
            var text = recents[i];

            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, Foreground = Brushes.White },
            });
            label.Children.Add(new TextBlock
            {
                Text = text,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            RecentPanel.Children.Add(CreateChip(label, () => Finish(text), text));

            int id = _hotkeys.TryRegister(ModifierKeys.Control | ModifierKeys.Alt, VkOne + (uint)i, () => Finish(text));
            if (id != 0)
            {
                _hotkeyIds.Add(id);
            }
        }

        HintText.Text = count > 0
            ? "Ctrl+Alt+1~5: 최근 설명 적용 · 입력창을 클릭하면 직접 입력 (Enter 적용, Esc 닫기)"
            : "입력창을 클릭하면 직접 입력할 수 있습니다 (Enter 적용, Esc 닫기)";
    }

    private Button CreateChip(object content, Action onClick, string? toolTip)
    {
        var button = new Button
        {
            Content = content,
            Style = (Style)FindResource("ChipButton"),
            ToolTip = toolTip,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void ApplyTag(string tag)
    {
        var text = InputBox.Text.Trim();
        Finish(text.Length == 0 ? tag : $"{tag}: {text}");
    }

    /// <summary>설명을 반영하고 닫는다. 빈 설명이면 아무것도 바꾸지 않고 닫는다(클립보드의 원본 이미지 유지).</summary>
    private void Finish(string? description)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        UnregisterHotkeys();

        description = description?.Trim();
        if (string.IsNullOrEmpty(description))
        {
            _timer.Stop();
            Close();
            return;
        }

        bool applied;
        try
        {
            applied = _applyDescription(description);
        }
        catch (Exception)
        {
            applied = false;
        }

        ShowResult(applied);
    }

    private void ShowResult(bool applied)
    {
        _resultShown = true;
        ContentPanel.Visibility = Visibility.Collapsed;
        HeaderText.Text = applied
            ? "✓ 설명이 이미지에 반영되었습니다"
            : "⚠ 설명을 반영하지 못했습니다 (원본 이미지는 클립보드에 그대로 있습니다)";
        _remainingMs = applied ? AppliedMs : FailedMs;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_resultShown && IsInteracting)
        {
            return;
        }

        _remainingMs -= TickMs;
        if (_remainingMs <= 0)
        {
            _timer.Stop();
            Close();
        }
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in _hotkeyIds)
        {
            _hotkeys.Unregister(id);
        }
        _hotkeyIds.Clear();
    }

    private void Cleanup()
    {
        _finished = true;
        _timer.Stop();
        UnregisterHotkeys();
    }

    private void SetNoActivate(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GwlExStyle);
        style = enabled ? style | WsExNoActivate | WsExToolWindow : style & ~WsExNoActivate;
        SetWindowLong(hwnd, GwlExStyle, style);
        _noActivate = enabled;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => SetNoActivate(true);

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Finish(null);
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // 입력창을 눌러 활성화했다가 다른 곳을 클릭한 경우: 적어 둔 설명이 있으면 반영하고 닫는다.
        Finish(InputBox.Text);
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _remainingMs = Math.Max(_remainingMs, GraceMs);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Finish(null);

    private void InputBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_noActivate)
        {
            SetNoActivate(false);
            Activate();
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            InputBox.Focus();
            Keyboard.Focus(InputBox);
        }), DispatcherPriority.Input);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = InputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Finish(InputBox.Text);
            e.Handled = true;
        }
    }
}
