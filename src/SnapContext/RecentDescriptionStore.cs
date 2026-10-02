using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SnapContext;

/// <summary>최근에 쓴 설명 5개를 %APPDATA%\SnapContext에 보관한다(토스트의 원클릭 재사용용).</summary>
public sealed class RecentDescriptionStore
{
    public const int MaxItems = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private readonly string _path;

    public RecentDescriptionStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SnapContext",
            "recent_descriptions.json");
    }

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<string>();
            }

            var items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path));
            return items?.Where(s => !string.IsNullOrWhiteSpace(s)).Take(MaxItems).ToList()
                ?? new List<string>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public void Add(string description)
    {
        var items = Load().Where(s => s != description).Prepend(description).Take(MaxItems).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 최근 설명은 편의 기능이라, 저장에 실패해도 캡처 흐름은 막지 않는다.
        }
    }
}
