using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapContext;

/// <summary>
/// 모든 클립보드 쓰기는 이 헬퍼를 거친다.
/// 규칙 B-10: Win+V 히스토리/클라우드 동기화 제외 플래그를 예외 없이 항상 적용한다(이미지와 글자 모두).
/// 다른 프로세스(클립보드 관리자 등)가 클립보드를 잡고 있으면 COMException이 나므로 짧게 재시도한다.
/// </summary>
public static class ClipboardHelper
{
    public const string CanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloudClipboard = "CanUploadToCloudClipboard";

    public static bool TrySetImage(BitmapSource image, int attempts = 6, int delayMs = 40) =>
        TrySet(data => data.SetImage(image), attempts, delayMs);

    /// <summary>인식한 화면 글자 등 텍스트를 복사한다. 글자에 민감한 내용이 있을 수 있어 같은 제외 플래그를 붙인다.</summary>
    public static bool TrySetText(string text, int attempts = 6, int delayMs = 40) =>
        TrySet(data => data.SetText(text, TextDataFormat.UnicodeText), attempts, delayMs);

    private static bool TrySet(Action<DataObject> fill, int attempts, int delayMs)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var data = new DataObject();
                fill(data);
                data.SetData(CanIncludeInClipboardHistory, new MemoryStream(BitConverter.GetBytes(0)));
                data.SetData(CanUploadToCloudClipboard, new MemoryStream(BitConverter.GetBytes(0)));
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(delayMs);
            }
        }
        return false;
    }
}
