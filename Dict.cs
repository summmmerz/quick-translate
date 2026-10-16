using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace QuickTranslate;

/// <summary>一条词库记录，字段顺序与 tools/BuildDict.py 写出的 payload 一致。</summary>
public sealed record DictEntry(
    string Phonetic, string Pos, string Translation, string Tag, string Exchange,
    string Collins, string Oxford, string Bnc, string Frq);

/// <summary>
/// 离线英汉词典（数据来自 ECDICT，MIT，见 tools/BuildDict.py）。dict.bin 与 exe 同目录，
/// 用 MemoryMappedFile 按页换入，所以词库多大都不占常驻内存；查询是对索引段做二分，
/// 约 16 次探测、亚毫秒级。
/// </summary>
public static class Dict
{
    private static readonly object Gate = new();
    private static MemoryMappedFile? _file;
    private static MemoryMappedViewAccessor? _view;
    private static int _count, _keyBase, _dataBase;

    private static readonly Dictionary<string, string> TagNames = new()
    {
        ["zk"] = "中考", ["gk"] = "高考", ["cet4"] = "四级", ["cet6"] = "六级",
        ["ky"] = "考研", ["toefl"] = "托福", ["ielts"] = "雅思", ["gre"] = "GRE",
    };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "dict.bin");

    /// <summary>只认「一个英文单词」：句子、中文、含空格的一律 false，交给在线翻译。</summary>
    public static bool IsSingleWord(string text)
    {
        if (text.Length is 0 or > 24) return false;
        char first = text[0];
        if (!IsLetter(first)) return false;
        for (int i = 1; i < text.Length; i++)
        {
            char c = text[i];
            if (!IsLetter(c) && c != '\'' && c != '-') return false;
        }
        return true;
    }

    private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>查词；不是单个英文词、词库缺失或没查到都返回 null（调用方回落在在线翻译）。</summary>
    public static DictEntry? Lookup(string text)
    {
        text = text.Trim();
        if (!IsSingleWord(text)) return null;

        try
        {
            EnsureOpen();
        }
        catch
        {
            return null;    // 词库缺失或损坏时静默降级，不打扰用户
        }

        byte[] want = Encoding.UTF8.GetBytes(text.ToLowerInvariant());
        byte[] slot = new byte[12];
        int lo = 0, hi = _count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            _view!.ReadArray(20 + mid * 12, slot, 0, 12);
            int keyLen = BitConverter.ToUInt16(slot, 4);
            byte[] key = new byte[keyLen];
            _view.ReadArray(_keyBase + BitConverter.ToInt32(slot, 0), key, 0, keyLen);

            int cmp = Compare(key, want);
            if (cmp == 0)
            {
                int dataLen = BitConverter.ToUInt16(slot, 10);
                byte[] payload = new byte[dataLen];
                _view.ReadArray(_dataBase + BitConverter.ToInt32(slot, 6), payload, 0, dataLen);
                return Parse(Encoding.UTF8.GetString(payload));
            }
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }
        return null;
    }

    /// <summary>考纲标签码（"zk gk cet4"）译成中文，用于卡片上那行小字。</summary>
    public static string TagText(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "";
        var parts = new List<string>();
        foreach (string t in tag.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            parts.Add(TagNames.TryGetValue(t, out string? name) ? name : t);
        return string.Join(" · ", parts);
    }

    private static DictEntry Parse(string payload)
    {
        string[] f = payload.Split('\x1f');
        string Get(int i) => i < f.Length ? f[i] : "";
        return new DictEntry(Get(0), Get(1), Get(2), Get(3), Get(4), Get(5), Get(6), Get(7), Get(8));
    }

    /// <summary>无符号逐字节比较，与 BuildDict.py 里 Python bytes 的排序一致。</summary>
    private static int Compare(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        return a.Length.CompareTo(b.Length);
    }

    private static void EnsureOpen()
    {
        if (_view != null) return;
        lock (Gate)
        {
            if (_view != null) return;
            if (!File.Exists(FilePath)) throw new FileNotFoundException("词库文件缺失", FilePath);

            _file = MemoryMappedFile.CreateFromFile(FilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            byte[] head = new byte[20];
            _view.ReadArray(0, head, 0, 20);
            if (head[0] != 'Q' || head[1] != 'D' || head[2] != 'I' || head[3] != 'C')
                throw new InvalidDataException("词库文件不是 QDIC 格式");
            _count = BitConverter.ToInt32(head, 8);
            _keyBase = BitConverter.ToInt32(head, 12);
            _dataBase = BitConverter.ToInt32(head, 16);
        }
    }
}
