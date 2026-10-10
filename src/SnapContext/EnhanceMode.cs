using System;

namespace SnapContext;

/// <summary>
/// 설명 보강 방식(실험 버전). 실행할 때 `--mode A|B`로 고르며, 아무것도 안 주면 Plain(AI 없음).
/// 예전 버전 C(질문 틀 채우기)는 프리셋으로 흡수되어 삭제되었다(CLAUDE.md 12절 2026-10-05).
/// </summary>
public enum EnhanceMode
{
    /// <summary>입력한 설명을 그대로 캡션으로 쓴다.</summary>
    Plain,

    /// <summary>A: 입력한 메모 글만 AI(Claude)로 보내 매끄러운 문장으로 다듬어 제안한다.</summary>
    TextRewrite,

    /// <summary>B: 스크린샷(과 메모)을 AI(Claude)로 보내 설명을 작성해 제안한다.</summary>
    ImageDescribe,
}

public static class EnhanceModes
{
    public static EnhanceMode FromArgs(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase))
            {
                return Parse(args[i + 1]);
            }
        }
        return EnhanceMode.Plain;
    }

    public static EnhanceMode Parse(string value) => value.Trim().ToUpperInvariant() switch
    {
        "A" or "TEXT" => EnhanceMode.TextRewrite,
        "B" or "IMAGE" => EnhanceMode.ImageDescribe,
        _ => EnhanceMode.Plain,
    };

    public static string Label(EnhanceMode mode) => mode switch
    {
        EnhanceMode.TextRewrite => "버전 A · 글 다듬기 (AI)",
        EnhanceMode.ImageDescribe => "버전 B · 이미지 분석 (AI)",
        _ => "",
    };

    public static bool UsesAi(EnhanceMode mode) => mode is EnhanceMode.TextRewrite or EnhanceMode.ImageDescribe;
}
