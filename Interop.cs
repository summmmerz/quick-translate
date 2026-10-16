using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace QuickTranslate;

internal static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;
    public const int VK_CONTROL = 0x11, VK_C = 0x43, VK_ESCAPE = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public Union U; }

    [StructLayout(LayoutKind.Explicit)]
    public struct Union
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

    public static void SendCtrlC()
    {
        var a = new INPUT[4];
        for (int i = 0; i < 4; i++) a[i].type = INPUT_KEYBOARD;
        a[0].U.ki.wVk = VK_CONTROL;
        a[1].U.ki.wVk = VK_C;
        a[2].U.ki.wVk = VK_C; a[2].U.ki.dwFlags = KEYEVENTF_KEYUP;
        a[3].U.ki.wVk = VK_CONTROL; a[3].U.ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(4, a, Marshal.SizeOf<INPUT>());
    }
}

/// <summary>全局低级鼠标钩子。回调里绝不做重活，一律异步转给 UI 线程。</summary>
internal sealed class MouseHook : IDisposable
{
    private readonly Native.LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    public event Action<int, int>? ButtonDown;
    public event Action<int, int>? ButtonUp;

    public MouseHook() => _proc = HookProc;

    public bool Install()
    {
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is Native.WM_LBUTTONDOWN or Native.WM_LBUTTONUP)
            {
                var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                int x = data.pt.X, y = data.pt.Y;
                var handler = msg == Native.WM_LBUTTONDOWN ? ButtonDown : ButtonUp;
                if (handler is not null)
                {
                    var app = Application.Current;
                    if (app is not null) app.Dispatcher.BeginInvoke(() => handler(x, y));
                    else handler(x, y);
                }
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}

internal static class Clip
{
    /// <summary>备份剪贴板 → 模拟 Ctrl+C → 取回选中文本 → 还原剪贴板。</summary>
    public static string? GrabSelection()
    {
        uint before = Native.GetClipboardSequenceNumber();
        string? backup = null;
        bool hadText = false;
        try
        {
            if (Clipboard.ContainsText())
            {
                backup = Clipboard.GetText();
                hadText = true;
            }
        }
        catch { }

        // 先让目标程序把这次 mouseup 处理完（浏览器/Office 的选区有时要等到下一次
        // 消息循环才生效），再发 Ctrl+C；焦点被抢走时第一次 Ctrl+C 常被丢掉，120ms 后补发一次。
        Thread.Sleep(30);
        Native.SendCtrlC();

        string? text = null;
        int resend = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 400)
        {
            Thread.Sleep(15);
            if (Native.GetClipboardSequenceNumber() == before)
            {
                if (resend == 0 && sw.ElapsedMilliseconds > 120) { Native.SendCtrlC(); resend = 1; }
                continue;
            }
            Thread.Sleep(20);                       // 等数据落稳
            for (int i = 0; i < 3 && text is null; i++)
            {
                try { text = Clipboard.GetText(); }
                catch { Thread.Sleep(15); }
            }
            break;
        }

        if (hadText && !string.IsNullOrEmpty(backup) && backup != text)
        {
            for (int i = 0; i < 3; i++)
            {
                try { Clipboard.SetText(backup!); break; }
                catch { Thread.Sleep(20); }
            }
        }

        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
