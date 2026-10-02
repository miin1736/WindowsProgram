using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapContext;

/// <summary>
/// 모든 클립보드 쓰기는 이 헬퍼를 거친다.
/// 규칙 B-10: Win+V 히스토리/클라우드 동기화 제외 플래그를 예외 없이 항상 적용한다.
/// 다른 프로세스(클립보드 관리자 등)가 클립보드를 잡고 있으면 COMException이 나므로 짧게 재시도한다.
/// </summary>
public static class ClipboardHelper
{
    public const string CanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloudClipboard = "CanUploadToCloudClipboard";

    public static bool TrySetImage(BitmapSource image, int attempts = 6, int delayMs = 40)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(image);
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
