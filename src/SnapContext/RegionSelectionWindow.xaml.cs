using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapContext;

/// <summary>
/// 가상 화면 전체를 덮는 반투명 오버레이에서 드래그로 캡처 영역을 선택한다.
/// Esc로 즉시 취소 가능(규칙 A-3과 동일한 "즉시 취소" 원칙을 선택 단계에도 적용).
///
/// 프리셋: 위쪽에 프리셋 목록을 보여 주고 숫자키 1~9로 이번 캡처에 쓸 프리셋을, 0으로 "설명 없음"을 고른다.
/// 아무 키도 누르지 않으면 기본 프리셋이 적용되므로 입력은 전혀 필요 없다(규칙 A-1).
/// 숫자키는 이 오버레이가 활성일 때만 받으므로 다른 앱의 입력을 가로채지 않는다.
/// 안내 막대는 마우스 입력을 받지 않아 그 아래에서도 드래그할 수 있다.
///
/// 취소와 숫자키는 이 창이 키보드 포커스를 못 받아도 동작해야 한다: 윈도우의 전경 창 규칙 때문에 단축키를 눌러도
/// 선택 화면이 맨 앞 창이 되지 못하는 경우가 있고(실측), 그러면 Esc가 다른 앱으로 간다. 그래서 선택 화면이 떠 있는
/// 동안에만 Esc와 숫자키를 전역 단축키로 등록하고(닫히면 즉시 해제), 마우스 오른쪽 클릭으로도 취소할 수 있게 한다.
/// 선택 화면은 화면 전체를 덮은 채 이 키들을 안내하고 있으므로 이 몇 초 동안 키를 가져가는 것은 의도된 동작이다.
/// </summary>
public partial class RegionSelectionWindow : Window
{
    private static readonly Brush ChipOn = new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static readonly Brush ChipOff = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x41));

    private readonly IReadOnlyList<Preset> _presets;
    private readonly List<(string Id, Border Chip)> _chips = new();
    private Point _startPointDip;
    private bool _isSelecting;
    private string _selectedId;

    /// <summary>선택된 영역(물리 픽셀 기준, GDI 캡처에 바로 사용 가능).</summary>
    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

    /// <summary>이번 캡처에 적용할 프리셋. "설명 없음"이면 null.</summary>
    public Preset? SelectedPreset => _presets.FirstOrDefault(p => p.Id == _selectedId);

    private readonly HotkeyManager? _hotkeys;
    private readonly List<int> _hotkeyIds = new();
    private bool _finished;

    public RegionSelectionWindow(PresetSet? presets = null, HotkeyManager? hotkeys = null)
    {
        _hotkeys = hotkeys;
        InitializeComponent();

        _presets = presets?.Presets ?? Array.Empty<Preset>();
        _selectedId = presets?.DefaultPresetId ?? PresetStore.NonePresetId;
        BuildPresetBar();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += (_, _) => Cancel();
        ContentRendered += (_, _) =>
        {
            RegisterKeyHotkeys();
            Activate();
            Focus();
        };
        Closed += (_, _) => ReleaseKeyHotkeys();
    }

    /// <summary>화면 위쪽 가운데(주 모니터 기준)에 프리셋 목록과 조작 안내를 그린다.</summary>
    private void BuildPresetBar()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = _presets.Count > 0
                ? "드래그로 영역을 선택하세요 · 숫자키로 설명 선택 · Esc 취소"
                : "드래그로 영역을 선택하세요 · Esc 취소",
            Foreground = Brushes.White,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        if (_presets.Count > 0)
        {
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 0; i < _presets.Count; i++)
            {
                row.Children.Add(MakeChip(_presets[i].Id, $"{i + 1}  {_presets[i].Name}"));
            }

            row.Children.Add(MakeChip(PresetStore.NonePresetId, "0  설명 없음"));
            panel.Children.Add(row);
        }

        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x21, 0x24)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = SystemParameters.PrimaryScreenWidth * 0.9,
            Child = panel,
        };

        // 가상 화면 좌표계에서 주 모니터의 위쪽 가운데에 놓는다(모니터 배치에 따라 Left/Top이 음수일 수 있다).
        var host = new Grid { Width = SystemParameters.PrimaryScreenWidth, IsHitTestVisible = false, Name = "PresetBarHost" };
        host.Children.Add(bar);
        Canvas.SetLeft(host, -SystemParameters.VirtualScreenLeft);
        Canvas.SetTop(host, -SystemParameters.VirtualScreenTop + 16);
        RootCanvas.Children.Add(host);
        _presetBarHost = host;
        UpdateChips();
    }

    private Grid? _presetBarHost;

    private Border MakeChip(string id, string text)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12 },
        };
        _chips.Add((id, chip));
        return chip;
    }

    private void UpdateChips()
    {
        foreach (var (id, chip) in _chips)
        {
            chip.Background = id == _selectedId ? ChipOn : ChipOff;
        }
    }

    /// <summary>숫자키로 프리셋을 고른다. 목록에 없는 번호는 무시한다.</summary>
    public void SelectDigit(int digit)
    {
        if (digit == 0)
        {
            _selectedId = PresetStore.NonePresetId;
        }
        else if (digit <= _presets.Count)
        {
            _selectedId = _presets[digit - 1].Id;
        }
        else
        {
            return;
        }

        UpdateChips();
    }

    public static int DigitOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => -1,
    };

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPointDip = e.GetPosition(this);
        _isSelecting = true;

        Canvas.SetLeft(SelectionRect, _startPointDip.X);
        Canvas.SetTop(SelectionRect, _startPointDip.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;

        var current = e.GetPosition(this);
        var x = Math.Min(current.X, _startPointDip.X);
        var y = Math.Min(current.Y, _startPointDip.Y);
        var w = Math.Abs(current.X - _startPointDip.X);
        var h = Math.Abs(current.Y - _startPointDip.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        _isSelecting = false;

        var endPointDip = e.GetPosition(this);
        var dipRect = new Rect(
            Math.Min(_startPointDip.X, endPointDip.X),
            Math.Min(_startPointDip.Y, endPointDip.Y),
            Math.Abs(endPointDip.X - _startPointDip.X),
            Math.Abs(endPointDip.Y - _startPointDip.Y));

        if (dipRect.Width < 2 || dipRect.Height < 2)
        {
            // 드래그 없이 클릭만 한 경우 등 - 선택 무시하고 오버레이 유지
            SelectionRect.Visibility = Visibility.Collapsed;
            _isSelecting = false;
            return;
        }

        SelectedRegion = ToPhysicalPixels(dipRect);

        // 안내 막대와 선택 사각형이 캡처 이미지에 섞이지 않도록 닫기 직전에 숨긴다.
        SelectionRect.Visibility = Visibility.Collapsed;
        if (_presetBarHost is not null)
        {
            _presetBarHost.Visibility = Visibility.Hidden;
        }

        _finished = true;
        ReleaseKeyHotkeys();
        DialogResult = true;
        Close();
    }

    /// <summary>취소한다(Esc, 오른쪽 클릭). 이미 끝났으면 아무것도 하지 않는다.</summary>
    public void Cancel()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        SelectedRegion = null;
        ReleaseKeyHotkeys();

        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            // 대화상자로 열리지 않은 경우(시험 등)에는 결과 없이 닫기만 한다.
        }

        Close();
    }

    /// <summary>
    /// 선택 화면이 떠 있는 동안에만 Esc와 숫자키(윗줄과 키패드)를 전역 단축키로 등록한다.
    /// 등록에 실패한 키는 건너뛴다(그 경우에도 창이 포커스를 받으면 KeyDown으로 동작한다).
    /// 핸들러는 디스패처로 넘긴다(WM_HOTKEY 처리 안에서 창을 닫지 않기 위해).
    /// </summary>
    public void RegisterKeyHotkeys()
    {
        if (_hotkeys is null || _hotkeyIds.Count > 0)
        {
            return;
        }

        void Add(uint vk, Action action)
        {
            int id = _hotkeys.TryRegister(ModifierKeys.None, vk, () => Dispatcher.BeginInvoke(action));
            if (id != 0)
            {
                _hotkeyIds.Add(id);
            }
        }

        Add(0x1B, Cancel); // VK_ESCAPE
        for (int d = 0; d <= 9; d++)
        {
            int digit = d;
            Add((uint)(0x30 + d), () => SelectDigit(digit));  // 윗줄 0~9
            Add((uint)(0x60 + d), () => SelectDigit(digit));  // 키패드 0~9
        }
    }

    /// <summary>등록한 전역 단축키를 모두 해제한다. 선택 화면이 닫힐 때 반드시 호출된다.</summary>
    public void ReleaseKeyHotkeys()
    {
        if (_hotkeys is null)
        {
            _hotkeyIds.Clear();
            return;
        }

        foreach (var id in _hotkeyIds)
        {
            _hotkeys.Unregister(id);
        }

        _hotkeyIds.Clear();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            return;
        }

        int digit = DigitOf(e.Key);
        if (digit >= 0)
        {
            SelectDigit(digit);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 창 기준 DIP(Device-Independent Pixel) 좌표를 가상 화면 전체의 물리 픽셀 좌표로 변환한다.
    /// System DPI Aware 모드(이 프로젝트의 기본값) 기준이며, 모니터마다 배율이 다른 Per-Monitor
    /// 환경에서는 오차가 생길 수 있음 — Phase 1 이후 PMv2 전환 시 재검증 필요(PROGRESS.md 참고).
    /// </summary>
    private System.Drawing.Rectangle ToPhysicalPixels(Rect dipRectRelativeToWindow)
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
            ?? Matrix.Identity;

        var topLeftDip = new Point(Left + dipRectRelativeToWindow.Left, Top + dipRectRelativeToWindow.Top);
        var bottomRightDip = new Point(Left + dipRectRelativeToWindow.Right, Top + dipRectRelativeToWindow.Bottom);

        var topLeftPx = transform.Transform(topLeftDip);
        var bottomRightPx = transform.Transform(bottomRightDip);

        return new System.Drawing.Rectangle(
            (int)Math.Round(topLeftPx.X),
            (int)Math.Round(topLeftPx.Y),
            (int)Math.Round(bottomRightPx.X - topLeftPx.X),
            (int)Math.Round(bottomRightPx.Y - topLeftPx.Y));
    }
}
