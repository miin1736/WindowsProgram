using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapContext;

/// <summary>
/// AI 기능으로 데이터를 외부(Anthropic API)에 보내도 되는지에 대한 사용자 동의를 이 PC에 저장한다.
/// 종류는 "text"(메모 글만)와 "image"(스크린샷 포함)로 나뉘며, 동의는 종류별로 따로 받는다.
/// 동의를 취소하려면 settings.json의 해당 항목을 지우면 된다.
/// </summary>
public sealed class AiConsentStore
{
    private sealed class SettingsFile
    {
        [JsonPropertyName("ai_consent")]
        public List<string> AiConsent { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public AiConsentStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SnapContext",
            "settings.json");
    }

    public bool Has(string kind) => Load().AiConsent.Contains(kind);

    public void Grant(string kind)
    {
        var settings = Load();
        if (settings.AiConsent.Contains(kind))
        {
            return;
        }

        settings.AiConsent.Add(kind);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장에 실패하면 다음 사용 때 다시 묻는다(동의 없이 보내는 쪽으로는 실패하지 않는다).
        }
    }

    private SettingsFile Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new SettingsFile();
            }

            var settings = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path)) ?? new SettingsFile();
            settings.AiConsent = settings.AiConsent.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SettingsFile();
        }
    }
}
