using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickTranslate;

/// <summary>全局热键：解析 "Ctrl+Alt+Q" 这类描述，注册到指定窗口的消息循环。</summary>
internal sealed class Hotkeys : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _src;
    private readonly Dictionary<int, Action> _map = new();
    private int _next = 0xC100;

    public Hotkeys(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _src = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("窗口句柄还没就绪");
        _src.AddHook(WndProc);
    }

    public bool Register(string spec, Action action)
    {
        int id = RegisterTemp(spec, action);
        return id != 0;
    }

    /// <summary>临时热键（比如弹窗可见时抢占 Esc），用完 Unregister。</summary>
    public int RegisterTemp(string spec, Action action)
    {
        if (!TryParse(spec, out var mods, out var vk)) return 0;
        int id = _next++;
        if (!Native.RegisterHotKey(_hwnd, id, mods | Native.MOD_NOREPEAT, vk)) return 0;
        _map[id] = action;
        return id;
    }

    public void Unregister(int id)
    {
        if (id == 0) return;
        _map.Remove(id);
        Native.UnregisterHotKey(_hwnd, id);
    }

    public static bool TryParse(string spec, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "alt": mods |= Native.MOD_ALT; break;
                case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= 0x0008; break;
                default: return false;
            }
        }

        var last = parts[^1];
        try
        {
            var key = (Key)new KeyConverter().ConvertFromString(last)!;
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        }
        catch { return false; }

        return mods != 0 && vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _map.TryGetValue(w.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _map.Keys.ToList()) Native.UnregisterHotKey(_hwnd, id);
        _map.Clear();
        _src.RemoveHook(WndProc);
    }
}
