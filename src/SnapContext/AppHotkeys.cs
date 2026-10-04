namespace SnapContext;

/// <summary>
/// 앱이 상시 등록하는 전역 핫키. 재설정 UI(Week 4)가 생기기 전까지는 고정값이다(규칙 C-12: 출시 전 재설정 UI 필수).
/// 토스트가 떠 있는 동안에만 쓰는 Ctrl+Alt+1~5는 CaptureToastWindow가 따로 등록한다.
/// </summary>
public static class AppHotkeys
{
    public const uint VkCapture = 0x53; // S
    public const uint VkCopyDescription = 0x43; // C

    public const string CaptureText = "Ctrl+Alt+S";
    public const string CopyDescriptionText = "Ctrl+Alt+C";
}
