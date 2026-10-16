using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickTranslate;

public static class Translator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    // 只做应用真正需要区分的几类脚本判断，不是通用语言识别
    private static readonly Regex Kana = new(@"[\u3040-\u30ff\uff66-\uff9f]", RegexOptions.Compiled);
    private static readonly Regex Hangul = new(@"[\u1100-\u11ff\uac00-\ud7af]", RegexOptions.Compiled);
    private static readonly Regex Han = new(@"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]", RegexOptions.Compiled);
    private static readonly Regex Cyrillic = new(@"[\u0400-\u04ff]", RegexOptions.Compiled);

    /// <summary>粗判原文语种，返回 zh / ja / ko / ru / en。</summary>
    public static string DetectSource(string text)
    {
        if (Kana.IsMatch(text)) return "ja";       // 有假名就是日文（日文里也有汉字，得先判）
        if (Hangul.IsMatch(text)) return "ko";
        if (Han.IsMatch(text)) return "zh";
        if (Cyrillic.IsMatch(text)) return "ru";
        return "en";
    }

    /// <summary>解析最终目标语种：auto 时中文译英、其余一律译中。</summary>
    public static string ResolveTarget(string text, string? targetLang)
        => string.IsNullOrEmpty(targetLang) || targetLang == "auto"
            ? (DetectSource(text) == "zh" ? "en" : "zh")
            : targetLang;

    /// <summary>界面上的单字缩写，如 中 / 英 / 日。</summary>
    public static string ShortName(string code) => code switch
    {
        "zh" => "中",
        "en" => "英",
        "ja" => "日",
        "ko" => "韩",
        "fr" => "法",
        "de" => "德",
        "ru" => "俄",
        "es" => "西",
        _ => code,
    };

    public static async Task<string> TranslateAsync(string text, Config cfg, CancellationToken ct = default)
    {
        var target = Languages.Get(ResolveTarget(text, cfg.TargetLang)).Prompt;

        var payload = new
        {
            model = cfg.Model,
            temperature = 1.3,
            stream = false,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "You are a professional translation engine. Translate the user's text into " + target +
                        ". Output ONLY the translation: no explanation, no pinyin, no quotes around it, no notes. " +
                        "Preserve the original line breaks and formatting. If the input is a single word, output the most common translation(s) only."
                },
                new { role = "user", content = "Translate into " + target + ":\n\"\"\"\n" + text + "\n\"\"\"" }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.BaseUrl);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new Exception("HTTP " + (int)resp.StatusCode + " " + Shorten(raw, 180));

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return (content ?? "").Trim();
    }

    private static string Shorten(string s, int n)
    {
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }
}
