using System;
using System.Runtime.InteropServices;

namespace SnapContext;

/// <summary>
/// 작업 표시줄 알림 영역(트레이) 아이콘. 앱이 켜져 있는지 눈으로 알 수 있고, 프리셋 편집 창과 종료의 진입점이다.
/// 광고는 배치하지 않는다(트레이 메뉴는 규칙 A-4가 허용하는 위치이지만 지금은 광고 방식이 확정되지 않았다).
/// WinForms 형식은 WPF 형식과 이름이 겹치므로 이 파일에서만 전체 이름으로 쓴다.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;
    private readonly IntPtr _handle;

    public TrayIcon(string captureHotkeyText, Action onCapture, Action onEditPresets, Action onExit)
    {
        using var bitmap = DrawIcon();
        _handle = bitmap.GetHicon();
        _image = System.Drawing.Icon.FromHandle(_handle);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add($"캡처 ({captureHotkeyText})", null, (_, _) => onCapture());
        menu.Items.Add("프리셋 편집…", null, (_, _) => onEditPresets());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => onExit());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _image,
            Text = $"SnapContext — 캡처 {captureHotkeyText}",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => onEditPresets();
    }

    /// <summary>시험용: 메뉴 항목 글자 목록.</summary>
    public string[] MenuLabels
    {
        get
        {
            var items = _icon.ContextMenuStrip!.Items;
            var result = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                result[i] = items[i].Text;
            }
            return result;
        }
    }

    public bool Visible => _icon.Visible;

    /// <summary>시험용: 메뉴 항목을 클릭한 것과 같은 효과를 낸다.</summary>
    public void InvokeMenuItem(int index) => _icon.ContextMenuStrip!.Items[index].PerformClick();

    private static System.Drawing.Bitmap DrawIcon()
    {
        var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        using var back = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x20, 0x21, 0x24));
        g.FillEllipse(back, 1, 1, 30, 30);

        // 영역 선택 모서리 표시(캡처 도구임을 나타내는 간단한 그림)
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x4F, 0x8E, 0xF7), 3f);
        g.DrawLine(pen, 8, 14, 8, 8);
        g.DrawLine(pen, 8, 8, 14, 8);
        g.DrawLine(pen, 24, 18, 24, 24);
        g.DrawLine(pen, 24, 24, 18, 24);
        return bmp;
    }

    public void Dispose()
    {
        // 앱이 끝난 뒤에도 아이콘이 알림 영역에 남는 일이 없도록 반드시 숨기고 해제한다.
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
        DestroyIcon(_handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
