using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SnapContext;

/// <summary>프리셋 하나: 이름과, 캡처 때 이미지에 합성할 설명문.</summary>
public sealed record Preset(string Id, string Name, string Caption);

/// <summary>프리셋 목록과 기본 프리셋. "설명 없음"(<see cref="PresetStore.NonePresetId"/>)은 항상 내장되어 목록에는 없다.</summary>
public sealed record PresetSet(IReadOnlyList<Preset> Presets, string DefaultPresetId);

/// <summary>
/// 프리셋을 %APPDATA%\SnapContext\presets.json에 보관한다(이 PC 안에서만, 외부 전송 없음).
/// 캡처는 어떤 경우에도 실패하면 안 되므로(규칙 A-1) <see cref="Load"/>는 예외를 던지지 않고,
/// 파일이 없거나 깨졌으면 기본 프리셋으로 복구한다.
/// </summary>
public sealed class PresetStore
{
    public const string NonePresetId = "none";
    public const int MaxPresets = 9;       // 오버레이 숫자키 1~9에 대응
    public const int MaxNameLength = 20;
    public const int MaxCaptionLength = 300;

    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public PresetStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SnapContext",
            "presets.json");
    }

    public string FilePath => _path;

    /// <summary>처음 실행할 때의 기본 프리셋. 기본값은 가장 범용적인 "설명 요청"이다(사용자가 편집 창에서 바꾼다).</summary>
    public static PresetSet Defaults() => new(
        new Preset[]
        {
            new("bug", "버그", "이 화면에서 버그나 비정상적인 동작으로 보이는 부분을 찾아 원인과 해결 방법을 알려주세요."),
            new("ui", "UI 검토", "이 화면의 UI/UX를 검토하고 개선할 점을 구체적으로 제안해 주세요."),
            new("error", "에러 해결", "이 화면의 에러 메시지를 읽고 원인과 해결 방법을 단계별로 알려주세요."),
            new("describe", "설명 요청", "이 화면에 무엇이 있는지 설명하고, 눈에 띄는 문제가 있으면 알려주세요."),
        },
        "describe");

    /// <summary>"설명 없음"이면 null, 아니면 기본 프리셋을 돌려준다.</summary>
    public static Preset? FindDefault(PresetSet set) =>
        set.Presets.FirstOrDefault(p => p.Id == set.DefaultPresetId);

    /// <summary>숫자키 1~9로 고르는 프리셋(목록 순서). 범위 밖이면 null.</summary>
    public static Preset? FindByDigit(PresetSet set, int digit) =>
        digit >= 1 && digit <= set.Presets.Count ? set.Presets[digit - 1] : null;

    public PresetSet Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Defaults();
            }

            var dto = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), JsonOptions);
            if (dto?.Presets is null)
            {
                BackupCorruptFile();
                return Defaults();
            }

            var normalized = Normalize(new PresetSet(
                dto.Presets.Select(p => new Preset(p.Id ?? "", p.Name ?? "", p.Caption ?? "")).ToList(),
                dto.DefaultPresetId ?? NonePresetId));

            // 목록이 전부 무효라 비었는데 파일에는 항목이 있었다면 손상으로 본다.
            if (normalized.Presets.Count == 0 && dto.Presets.Count > 0)
            {
                BackupCorruptFile();
                return Defaults();
            }

            return normalized;
        }
        catch (Exception)
        {
            BackupCorruptFile();
            return Defaults();
        }
    }

    /// <summary>검증한 뒤 임시 파일에 쓰고 교체한다(쓰는 도중 꺼져도 기존 파일이 깨지지 않게).</summary>
    public bool TrySave(PresetSet set, out string? error)
    {
        error = Validate(set);
        if (error is not null)
        {
            return false;
        }

        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);

            var model = new FileModel
            {
                Version = FormatVersion,
                DefaultPresetId = set.DefaultPresetId,
                Presets = set.Presets.Select(p => new PresetModel { Id = p.Id, Name = p.Name, Caption = p.Caption }).ToList(),
            };

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(model, JsonOptions));
            if (File.Exists(_path))
            {
                File.Replace(tmp, _path, null);
            }
            else
            {
                File.Move(tmp, _path);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = "프리셋을 저장하지 못했습니다: " + ex.Message;
            return false;
        }
    }

    /// <summary>저장 전 검사. 문제가 있으면 사용자에게 보여 줄 한국어 메시지를, 없으면 null을 반환한다.</summary>
    public static string? Validate(PresetSet set)
    {
        if (set.Presets.Count > MaxPresets)
        {
            return $"프리셋은 최대 {MaxPresets}개까지 만들 수 있습니다.";
        }

        var ids = new HashSet<string>();
        foreach (var p in set.Presets)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                return "이름이 비어 있는 프리셋이 있습니다.";
            }

            if (p.Name.Trim().Length > MaxNameLength)
            {
                return $"이름은 {MaxNameLength}자 이내로 적어 주세요: {p.Name.Trim()}";
            }

            if (string.IsNullOrWhiteSpace(p.Caption))
            {
                return $"'{p.Name.Trim()}' 프리셋의 설명이 비어 있습니다.";
            }

            if (p.Caption.Trim().Length > MaxCaptionLength)
            {
                return $"'{p.Name.Trim()}' 프리셋의 설명은 {MaxCaptionLength}자 이내로 적어 주세요.";
            }

            if (string.IsNullOrWhiteSpace(p.Id) || p.Id == NonePresetId || !ids.Add(p.Id))
            {
                return "프리셋 식별자가 올바르지 않습니다. 프리셋을 다시 만들어 주세요.";
            }
        }

        if (set.DefaultPresetId != NonePresetId && !ids.Contains(set.DefaultPresetId))
        {
            return "기본 프리셋이 목록에 없습니다.";
        }

        return null;
    }

    /// <summary>파일을 직접 고쳐서 생길 수 있는 문제(공백, 길이 초과, 중복, 9개 초과, 잘못된 기본값)를 조용히 바로잡는다.</summary>
    public static PresetSet Normalize(PresetSet set)
    {
        var result = new List<Preset>();
        var ids = new HashSet<string>();

        foreach (var p in set.Presets)
        {
            var name = p.Name.Trim();
            var caption = p.Caption.Trim();
            if (name.Length == 0 || caption.Length == 0)
            {
                continue;
            }

            if (name.Length > MaxNameLength)
            {
                name = name[..MaxNameLength];
            }

            if (caption.Length > MaxCaptionLength)
            {
                caption = caption[..MaxCaptionLength];
            }

            var id = p.Id.Trim();
            if (id.Length == 0 || id == NonePresetId || !ids.Add(id))
            {
                id = NewId(ids);
            }

            result.Add(new Preset(id, name, caption));
            if (result.Count == MaxPresets)
            {
                break;
            }
        }

        var defaultId = set.DefaultPresetId == NonePresetId || result.Any(p => p.Id == set.DefaultPresetId)
            ? set.DefaultPresetId
            : NonePresetId;

        return new PresetSet(result, defaultId);
    }

    /// <summary>편집 창에서 새 프리셋을 만들 때 쓰는 식별자.</summary>
    public static string NewId(ISet<string>? taken = null)
    {
        string id;
        do
        {
            id = "p" + Guid.NewGuid().ToString("N")[..8];
        }
        while (taken is not null && !taken.Add(id));
        return id;
    }

    private void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Copy(_path, _path + ".bad", overwrite: true);
            }
        }
        catch (Exception)
        {
            // 백업 실패는 무시한다. 복구 자체가 목적이다.
        }
    }

    private sealed class FileModel
    {
        public int Version { get; set; }
        public string? DefaultPresetId { get; set; }
        public List<PresetModel>? Presets { get; set; }
    }

    private sealed class PresetModel
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Caption { get; set; }
    }
}
