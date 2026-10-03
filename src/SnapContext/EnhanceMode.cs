using System;

namespace SnapContext;

/// <summary>
/// 설명 보강 방식(실험 버전). 실행할 때 `--mode C|A|B`로 고르며, 아무것도 안 주면 Plain(입력한 글 그대로).
/// </summary>
public enum EnhanceMode
{
    /// <summary>입력한 설명을 그대로 캡션으로 쓴다.</summary>
    Plain,

    /// <summary>C: 태그를 누르면 AI 없이 완성된 질문 문장 틀에 메모를 끼워 넣는다.</summary>
    Template,

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
        "C" or "TEMPLATE" => EnhanceMode.Template,
        "A" or "TEXT" => EnhanceMode.TextRewrite,
        "B" or "IMAGE" => EnhanceMode.ImageDescribe,
        _ => EnhanceMode.Plain,
    };

    public static string Label(EnhanceMode mode) => mode switch
    {
        EnhanceMode.Template => "버전 C · 질문 틀 채우기",
        EnhanceMode.TextRewrite => "버전 A · 글 다듬기 (AI)",
        EnhanceMode.ImageDescribe => "버전 B · 이미지 분석 (AI)",
        _ => "",
    };

    public static bool UsesAi(EnhanceMode mode) => mode is EnhanceMode.TextRewrite or EnhanceMode.ImageDescribe;
}
