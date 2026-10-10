using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
///
/// 프리셋: 캡처할 때 이미 프리셋 설명이 합성되어 클립보드에 올라가 있다. 여기서 다른 프리셋 칩을 누르면 설명이 바뀐다.
///
/// 설명 보강 버전(EnhanceMode):
/// - TextRewrite(A) / ImageDescribe(B): 입력한 설명은 먼저 그대로 반영(즉시 사용 가능)하고,
///   AI 제안은 뒤따라 도착하면 "제안"으로만 보여준다. 사용자가 "이 문장으로 교체"를 눌러야 클립보드가 바뀐다.
/// </summary>
public partial class CaptureToastWindow : Window
{
    private const uint VkOne = 0x31;
    private const int DisplayMs = 4000;
    private const int GraceMs = 1500;
    private const int TickMs = 100;
    private const int AppliedMs = 2000;

    private const int FailedMs = 3000;
    private const int SuggestionDecisionMs = 10000;
    private const int DraftHoldMs = 8000;
    private const double ScreenMargin = 8;

    private readonly Func<string, bool> _applyDescription;
    private readonly Func<string?, CancellationToken, Task<AiResult>>? _requestSuggestion;
    private readonly EnhanceMode _mode;
    private readonly List<int> _hotkeyIds = new();
    private readonly HotkeyManager _hotkeys;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(TickMs) };

    private int _remainingMs = DisplayMs;
    private bool _noActivate = true;
    private bool _finished;
    private bool _closingPhase;
    private bool _holdCountdown;
    private bool _closed;
    private bool _appliedVerbatim;
    private bool _hasPresets;
    private string? _suggestion;

    /// <param name="applyDescription">설명을 이미지에 반영하고 클립보드를 갱신한다. 성공 여부를 반환한다.</param>
    /// <param name="requestSuggestion">A/B 버전에서 AI 제안을 받아 오는 함수(메모 → 제안). 다른 버전에서는 null.</param>
    public CaptureToastWindow(
        HotkeyManager hotkeys,
        IReadOnlyList<string> recents,
        IReadOnlyList<Preset> presets,
        Preset? appliedPreset,
        bool copied,
        Func<string, bool> applyDescription,
        EnhanceMode mode = EnhanceMode.Plain,
        Func<string?, CancellationToken, Task<AiResult>>? requestSuggestion = null)
    {
        InitializeComponent();

        _hotkeys = hotkeys;
        _applyDescription = applyDescription;
        _mode = mode;
        _requestSuggestion = EnhanceModes.UsesAi(mode) ? requestSuggestion : null;

        if (mode != EnhanceMode.Plain)
        {
            ModeBadge.Text = EnhanceModes.Label(mode);
            ModeBadge.Visibility = Visibility.Visible;
        }

        if (!copied)
        {
            HeaderText.Text = "⚠ 클립보드에 복사하지 못했습니다. 다시 캡처해 주세요.";
            ContentPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeaderText.Text = appliedPreset is null
                ? "✓ 캡처 완료 — 클립보드에 복사됨"
                : $"✓ 캡처 완료 — [{appliedPreset.Name}] 설명이 포함되어 복사됨";
            _hasPresets = presets.Count > 0;
            BuildChips(recents, presets, appliedPreset);
        }

        _timer.Tick += OnTick;
        _timer.Start();
        Closed += (_, _) => Cleanup();
    }

    private bool IsInteracting => IsMouseOver || InputBox.IsKeyboardFocusWithin;

    private void BuildChips(IReadOnlyList<string> recents, IReadOnlyList<Preset> presets, Preset? applied)
    {
        if (_mode == EnhanceMode.ImageDescribe && _requestSuggestion is not null)
        {
            TagPanel.Children.Add(CreateChip(
                new TextBlock { Text = "✨ AI가 설명 작성" },
                () => Finish(InputBox.Text, forceAi: true),
                "스크린샷을 AI(Anthropic)로 보내 설명을 작성합니다"));
        }

        foreach (var preset in presets)
        {
            var captured = preset;
            string label = applied is not null && applied.Id == preset.Id ? "✓ " + preset.Name : preset.Name;
            TagPanel.Children.Add(CreateChip(new TextBlock { Text = label }, () => ApplyPreset(captured), preset.Caption));
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

        HintText.Text = BuildHint(count);
    }

    private string BuildHint(int recentCount)
    {
        string basics = recentCount > 0
            ? "Ctrl+Alt+1~5: 최근 설명 적용 · 입력창을 클릭하면 직접 입력 (Enter 적용, Esc 닫기)"
            : "입력창을 클릭하면 직접 입력할 수 있습니다 (Enter 적용, Esc 닫기)";

        return _mode switch
        {
            EnhanceMode.TextRewrite => basics + "\n설명을 적용하면 그 글(이미지 제외)이 AI(Anthropic)로 전송되어 다듬은 제안을 받습니다.",
            EnhanceMode.ImageDescribe => basics + "\n✨ 버튼이나 설명 적용 시 스크린샷이 AI(Anthropic)로 전송됩니다.",
            _ => (_hasPresets ? "프리셋을 누르면 이 이미지의 설명이 바뀝니다. " : "") + basics,
        };
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

    /// <summary>프리셋 칩: 그 프리셋의 설명으로 바꾼다. 입력창에 적어 둔 글이 있으면 "참고:"로 덧붙인다.</summary>
    private void ApplyPreset(Preset preset)
    {
        var memo = InputBox.Text.Trim();
        Finish(memo.Length == 0 ? preset.Caption : $"{preset.Caption} 참고: {memo}");
    }

    /// <summary>
    /// 설명을 반영하고 마무리한다. 빈 설명이면 아무것도 바꾸지 않고 닫는다(클립보드의 원본 이미지 유지).
    /// A/B 버전에서는 반영 뒤에 AI 제안을 요청한다(forceAi는 메모 없이 AI만 요청하는 B의 ✨ 버튼용).
    /// </summary>
    private void Finish(string? description, bool forceAi = false)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        UnregisterHotkeys();

        description = description?.Trim();
        bool hasText = !string.IsNullOrEmpty(description);
        bool wantsAi = _requestSuggestion is not null && (hasText || forceAi);

        if (!hasText && !wantsAi)
        {
            _timer.Stop();
            Close();
            return;
        }

        if (hasText)
        {
            _appliedVerbatim = TryApply(description!);
            if (!wantsAi || !_appliedVerbatim)
            {
                ShowResult(_appliedVerbatim);
                return;
            }
        }

        StartAi(hasText ? description : null);
    }

    private bool TryApply(string text)
    {
        try
        {
            return _applyDescription(text);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void StartAi(string? memo)
    {
        _holdCountdown = true;
        ContentPanel.Visibility = Visibility.Collapsed;
        AiPanel.Visibility = Visibility.Visible;

        HeaderText.Text = _appliedVerbatim ? "✓ 설명이 이미지에 반영되었습니다" : "✨ AI에게 요청했습니다";
        AiStatusText.Text = (_mode == EnhanceMode.ImageDescribe ? "✨ AI가 이미지를 분석하는 중…" : "✨ AI가 문장을 다듬는 중…")
            + (_appliedVerbatim ? " 원문은 이미 반영되어 있어 바로 붙여넣을 수 있습니다." : "");

        _ = RunAiAsync(memo);
    }

    private async Task RunAiAsync(string? memo)
    {
        AiResult result;
        try
        {
            result = await _requestSuggestion!(memo, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            result = new AiResult(false, "", "AI 제안을 받지 못했습니다.");
        }

        if (_closed)
        {
            return;
        }

        _holdCountdown = false;
        if (result.Ok && !string.IsNullOrWhiteSpace(result.Text))
        {
            ShowSuggestion(result.Text);
        }
        else
        {
            ShowAiError(result.Error ?? "AI 제안을 받지 못했습니다.");
        }
    }

    private void ShowSuggestion(string text)
    {
        _suggestion = text;
        AiStatusText.Text = "✨ AI 제안";
        SuggestionText.Text = text;
        SuggestionBorder.Visibility = Visibility.Visible;
        SuggestionButtons.Visibility = Visibility.Visible;
        _remainingMs = SuggestionDecisionMs;
    }

    private void ShowAiError(string message)
    {
        AiStatusText.Text = "⚠ " + message + (_appliedVerbatim ? " (입력한 설명은 그대로 반영되어 있습니다)" : "");
        _closingPhase = true;
        _remainingMs = FailedMs + 1000;
    }

    private void ShowResult(bool applied)
    {
        ShowFinal(
            applied
                ? "✓ 설명이 이미지에 반영되었습니다"
                : "⚠ 설명을 반영하지 못했습니다 (원본 이미지는 클립보드에 그대로 있습니다)",
            applied ? AppliedMs : FailedMs);
    }

    private void ShowFinal(string message, int showMs)
    {
        ContentPanel.Visibility = Visibility.Collapsed;
        AiPanel.Visibility = Visibility.Collapsed;
        HeaderText.Text = message;
        _closingPhase = true;
        _remainingMs = showMs;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_holdCountdown)
        {
            return;
        }

        if (!_closingPhase && IsInteracting)
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
        _closed = true;
        _finished = true;
        _timer.Stop();
        UnregisterHotkeys();
        _cts.Cancel();
    }

    private void SetNoActivate(bool enabled)
    {
        NoActivateStyle.Apply(new WindowInteropHelper(this).Handle, enabled);
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
            DismissOrClose();
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DismissOrClose();

    /// <summary>입력 단계면 설명 없이 닫고, 이미 반영/AI 단계면 AI 요청을 취소하고 즉시 닫는다.</summary>
    private void DismissOrClose()
    {
        if (_finished)
        {
            _timer.Stop();
            Close();
        }
        else
        {
            Finish(null);
        }
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestion is null || _closingPhase)
        {
            return;
        }

        var text = _suggestion;
        _suggestion = null;
        bool ok = TryApply(text);
        ShowFinal(
            ok
                ? "✓ AI 제안이 이미지에 반영되었습니다"
                : "⚠ AI 제안을 반영하지 못했습니다 (클립보드의 이미지는 그대로 있습니다)",
            ok ? AppliedMs : FailedMs);
    }

    private void KeepButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }

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
