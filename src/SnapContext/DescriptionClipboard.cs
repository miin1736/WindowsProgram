using System;

namespace SnapContext;

public enum DescriptionCopyResult
{
    /// <summary>이번 캡처에 적용된 설명이 없다(클립보드는 건드리지 않는다).</summary>
    NoDescription,

    Copied,
    Failed,
}

/// <summary>
/// "순차 붙여넣기"용: 가장 최근 캡처에 적용된 설명을 기억해 두었다가 단축키로 글자로 복사한다.
/// 이미지를 붙여넣은 뒤 이 설명을 글자로 이어 붙일 수 있게 해 준다(CLAUDE.md 3절 Phase 1).
/// 새 캡처를 하면 지워서, 이전 캡처의 설명이 새 이미지에 잘못 붙는 일이 없게 한다.
/// </summary>
public sealed class LastDescription
{
    private string? _text;

    public bool HasValue => _text is not null;

    public string? Text => _text;

    public void Set(string text) => _text = string.IsNullOrWhiteSpace(text) ? null : text;

    public void Clear() => _text = null;

    public DescriptionCopyResult TryCopy(Func<string, bool> copyText)
    {
        if (_text is null)
        {
            return DescriptionCopyResult.NoDescription;
        }

        return copyText(_text) ? DescriptionCopyResult.Copied : DescriptionCopyResult.Failed;
    }
}
