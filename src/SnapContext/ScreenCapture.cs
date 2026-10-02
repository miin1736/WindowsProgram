using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapContext;

/// <summary>
/// 규칙 C-14: GDI BitBlt(Graphics.CopyFromScreen)를 기본 캡처 방식으로 사용한다.
/// 권한 프롬프트 없이 즉시 캡처 가능(규칙 A-1의 "즉시 클립보드 반영" 전제 조건).
/// </summary>
public static class ScreenCapture
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>물리 픽셀 기준 화면 좌표 사각형을 캡처한다.</summary>
    public static Bitmap CaptureRegion(Rectangle physicalPixelRegion)
    {
        var bitmap = new Bitmap(physicalPixelRegion.Width, physicalPixelRegion.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(
                physicalPixelRegion.Left,
                physicalPixelRegion.Top,
                0, 0,
                physicalPixelRegion.Size,
                CopyPixelOperation.SourceCopy);
        }
        return bitmap;
    }

    /// <summary>System.Drawing.Bitmap을 WPF 클립보드(Clipboard.SetImage)에 쓸 수 있는 BitmapSource로 변환.</summary>
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }
}
