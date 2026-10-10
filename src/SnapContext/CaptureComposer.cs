using System;
using System.Diagnostics;
using System.Drawing;

namespace SnapContext;

/// <summary>캡처 직후 클립보드에 올릴 이미지를 만든다. 프리셋 설명이 있으면 캡션을 합성한다.</summary>
public static class CaptureComposer
{
    /// <summary>
    /// 클립보드에 올린다. 프리셋이 있으면 설명을 합성한 이미지를, 없거나 합성/복사에 실패하면 원본을 올린다.
    /// 합성이 어떤 이유로 실패해도 캡처 자체는 실패하지 않는다(규칙 A-1).
    /// </summary>
    /// <returns>복사 성공 여부와, 실제로 이미지에 설명이 합성된 프리셋(합성하지 못했으면 null).</returns>
    public static (bool Copied, Preset? Applied) Copy(Bitmap original, Preset? preset)
    {
        if (preset is not null)
        {
            try
            {
                using var composed = CaptionRenderer.Compose(original, preset.Caption);
                if (ClipboardHelper.TrySetImage(ScreenCapture.ToBitmapSource(composed)))
                {
                    return (true, preset);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"프리셋 합성 실패(원본으로 대체): {ex}");
            }
        }

        return (ClipboardHelper.TrySetImage(ScreenCapture.ToBitmapSource(original)), null);
    }
}
