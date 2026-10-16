using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickTranslate;

public sealed class Config
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com/chat/completions";
    public string Model { get; set; } = "deepseek-chat";
    public string ApiKey { get; set; } = "";
    public string HotkeySelection { get; set; } = "Alt+Q";
    public string HotkeyInput { get; set; } = "Alt+W";
    /// <summary>目标语言代码；"auto" = 按原文语种自动决定（中文→英，其余→中）。</summary>
    public string TargetLang { get; set; } = "auto";
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    [JsonIgnore]
    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickTranslate");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        Config c = new Config();
        try
        {
            if (File.Exists(FilePath))
            {
                var d = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath));
                if (d is not null) c = d;
            }
        }
        catch { /* 配置坏了就用默认值，不让程序起不来 */ }
        c.TryImportKeyFromPot(); // 没有 key 就从 Pot 的配置里捡一个（自检路径也要走到）
        return c;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>首次运行时从 Pot 的配置里捡一个 DeepSeek key，省得用户再填一遍。</summary>
    public bool TryImportKeyFromPot()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey)) return false;
        try
        {
            var pot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "com.pot-app.desktop", "config.json");
            if (!File.Exists(pot)) return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(pot));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.StartsWith("openai@", StringComparison.Ordinal)) continue;
                if (!prop.Value.TryGetProperty("apiKey", out var k)) continue;
                var v = k.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                {
                    ApiKey = v!;
                    Save();
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}

/// <summary>一个可选的目标语言。Name 是界面上显示的中文，Prompt 是喂给模型的英文语言名。</summary>
public sealed class Lang
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Prompt { get; init; } = "";
}

public static class Languages
{
    public static readonly Lang[] All =
    {
        new() { Code = "auto", Name = "自动",     Prompt = "" },
        new() { Code = "zh",   Name = "中文",     Prompt = "Simplified Chinese" },
        new() { Code = "en",   Name = "英文",     Prompt = "English" },
        new() { Code = "ja",   Name = "日文",     Prompt = "Japanese" },
        new() { Code = "ko",   Name = "韩文",     Prompt = "Korean" },
        new() { Code = "fr",   Name = "法文",     Prompt = "French" },
        new() { Code = "de",   Name = "德文",     Prompt = "German" },
        new() { Code = "ru",   Name = "俄文",     Prompt = "Russian" },
        new() { Code = "es",   Name = "西班牙文", Prompt = "Spanish" },
    };

    public static Lang Get(string? code)
    {
        foreach (var l in All)
            if (l.Code == code) return l;
        return All[0];
    }
}
