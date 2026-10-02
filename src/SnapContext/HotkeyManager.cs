using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SnapContext;

/// <summary>
/// RegisterHotKey P/Invoke 래퍼. 규칙 8-1/C-12: 등록 실패는 반환값으로 감지 가능해야 하며,
/// 실패 시 사용자에게 명확히 안내한다(재설정 UI는 이후 단계에서 구현).
/// 여러 핫키를 동시에 등록할 수 있다(캡처 핫키 + 토스트 표시 중에만 잠깐 쓰는 임시 핫키).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyManager(Window messageWindow)
    {
        var helper = new WindowInteropHelper(messageWindow);
        helper.EnsureHandle();
        _source = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("HwndSource를 생성할 수 없습니다.");
        _source.AddHook(WndProc);
    }

    /// <summary>핫키를 등록한다. 성공하면 등록 ID(1 이상), 다른 프로그램과 충돌 등으로 실패하면 0을 반환한다.</summary>
    public int TryRegister(ModifierKeys modifiers, uint virtualKey, Action handler)
    {
        uint fsModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) fsModifiers |= MOD_WIN;

        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, fsModifiers, virtualKey))
        {
            return 0;
        }

        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
        {
            UnregisterHotKey(_source.Handle, id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList())
        {
            Unregister(id);
        }
        _source.RemoveHook(WndProc);
    }
}
