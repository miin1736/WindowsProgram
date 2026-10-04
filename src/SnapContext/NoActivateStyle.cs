using System;
using System.Runtime.InteropServices;

namespace SnapContext;

/// <summary>
/// WS_EX_NOACTIVATE: 창을 띄우거나 클릭해도 다른 앱의 포커스를 뺏지 않게 한다(규칙 A-2).
/// WS_EX_TOOLWINDOW: 작업 표시줄과 Alt+Tab 목록에 나타나지 않게 한다.
/// </summary>
internal static class NoActivateStyle
{
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    public static void Apply(IntPtr hwnd, bool enabled)
    {
        int style = GetWindowLong(hwnd, GwlExStyle);
        style = enabled ? style | WsExNoActivate | WsExToolWindow : style & ~WsExNoActivate;
        SetWindowLong(hwnd, GwlExStyle, style);
    }
}
