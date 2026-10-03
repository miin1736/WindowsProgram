using System.Collections.Generic;

namespace SnapContext;

/// <summary>버전 C: AI 호출 없이 태그를 완성된 질문 문장으로 바꾼다(외부 전송 없음, 즉시).</summary>
public static class PromptTemplates
{
    private static readonly Dictionary<string, (string Instruction, string MemoLabel)> Templates = new()
    {
        ["버그"] = ("이 화면에서 버그나 비정상적인 동작으로 보이는 부분을 찾아 원인과 해결 방법을 알려주세요.", "상황"),
        ["UI 검토"] = ("이 화면의 UI/UX를 검토하고 개선할 점을 구체적으로 제안해 주세요.", "참고"),
        ["에러 로그"] = ("이 화면의 에러 메시지를 읽고 원인과 해결 방법을 단계별로 알려주세요.", "상황"),
    };

    /// <summary>알려진 태그면 질문 문장을, 아니면 null을 반환한다. 메모가 있으면 뒤에 붙인다.</summary>
    public static string? Build(string tag, string? memo)
    {
        if (!Templates.TryGetValue(tag, out var template))
        {
            return null;
        }

        var trimmed = memo?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? template.Instruction
            : $"{template.Instruction} {template.MemoLabel}: {trimmed}";
    }
}
