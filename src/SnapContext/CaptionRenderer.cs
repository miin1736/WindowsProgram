using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace SnapContext;

/// <summary>
/// 캡처 이미지 하단에 설명을 캡션으로 번인한 새 이미지를 만든다(Phase 1 기본값, CLAUDE.md 12절).
/// 클립보드에는 이미지 한 장만 올라가므로 붙여넣는 사이트와 무관하게 동작한다.
/// 폰트/색/레이아웃은 1차 안이며 이후 실사용 피드백으로 조정한다.
/// </summary>
public static class CaptionRenderer
{
    private const int MinCanvasWidth = 240;
    private static readonly Color StripColor = Color.FromArgb(0x20, 0x21, 0x24);
    private static readonly Color AccentColor = Color.FromArgb(0x4F, 0x8E, 0xF7);

    public static Bitmap Compose(Bitmap source, string caption)
    {
        int canvasWidth = Math.Max(source.Width, MinCanvasWidth);

        // 4K 캡처를 LLM이 축소해도 읽히도록 글자 크기를 이미지 폭에 비례시킨다.
        float fontPx = Math.Clamp(canvasWidth / 50f, 14f, 64f);
        float padding = fontPx * 0.8f;
        int accentHeight = Math.Max(2, (int)(fontPx / 8));
        float layoutWidth = canvasWidth - padding * 2;

        using var font = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat();

        float textHeight;
        using (var probe = new Bitmap(1, 1))
        using (var probeGraphics = Graphics.FromImage(probe))
        {
            probeGraphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            textHeight = probeGraphics.MeasureString(caption, font, new SizeF(layoutWidth, 100000f), format).Height;
        }

        int stripHeight = accentHeight + (int)Math.Ceiling(padding * 2 + textHeight);
        var result = new Bitmap(canvasWidth, source.Height + stripHeight, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(result);
        g.Clear(StripColor);

        // 원본 픽셀을 1:1로 복사한다(DPI 차이로 크기가 변하지 않도록 소스 사각형을 명시).
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(
            source,
            new Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        using (var accent = new SolidBrush(AccentColor))
        {
            g.FillRectangle(accent, 0, source.Height, canvasWidth, accentHeight);
        }

        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(
            caption,
            font,
            textBrush,
            new RectangleF(padding, source.Height + accentHeight + padding, layoutWidth, textHeight + 2),
            format);

        return result;
    }
}
