using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SnapContext;

/// <summary>
/// RegisterHotKey P/Invoke 래퍼. 규칙 8-1/C-12: 등록 실패는 반환값으로 감지 가능해야 하며,
/// 실패 시 사용자에게 명확히 안내한다(재설정 UI는 이후 단계에서 구현).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 1;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyManager(Window messageWindow)
    {
        var helper = new WindowInteropHelper(messageWindow);
        helper.EnsureHandle();
        _source = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("HwndSource를 생성할 수 없습니다.");
        _source.AddHook(WndProc);
    }

    public bool TryRegister(ModifierKeys modifiers, uint virtualKey)
    {
        uint fsModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) fsModifiers |= MOD_WIN;

        _registered = RegisterHotKey(_source.Handle, HotkeyId, fsModifiers, virtualKey);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
        _source.RemoveHook(WndProc);
    }
}
