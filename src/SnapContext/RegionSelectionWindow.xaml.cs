using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapContext;

/// <summary>
/// 가상 화면 전체를 덮는 반투명 오버레이에서 드래그로 캡처 영역을 선택한다.
/// Esc로 즉시 취소 가능(규칙 A-3과 동일한 "즉시 취소" 원칙을 선택 단계에도 적용).
/// </summary>
public partial class RegionSelectionWindow : Window
{
    private Point _startPointDip;
    private bool _isSelecting;

    /// <summary>선택된 영역(물리 픽셀 기준, GDI 캡처에 바로 사용 가능).</summary>
    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

    public RegionSelectionWindow()
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

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
        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedRegion = null;
            DialogResult = false;
            Close();
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
