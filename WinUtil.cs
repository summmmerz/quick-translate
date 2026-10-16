using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickTranslate;

internal static class WinUtil
{
    /// <summary>物理像素点是否落在窗口矩形里（Left/Top 是 DIP，要按该窗口所在屏幕的缩放换算）。</summary>
    public static bool Contains(Window w, int px, int py)
    {
        if (!w.IsVisible) return false;
        var (sx, sy) = Scale(w);
        double l = w.Left * sx, t = w.Top * sy;
        return px >= l && px <= l + w.ActualWidth * sx
            && py >= t && py <= t + w.ActualHeight * sy;
    }

    public static (double sx, double sy) Scale(Visual v)
    {
        var src = PresentationSource.FromVisual(v);
        if (src?.CompositionTarget is null) return (1, 1);
        var m = src.CompositionTarget.TransformToDevice;
        return (m.M11, m.M22);
    }

    /// <summary>把物理像素坐标换算成该窗口坐标系下的 DIP。</summary>
    public static Point ToDip(Window w, int px, int py)
    {
        var (sx, sy) = Scale(w);
        return new Point(px / sx, py / sy);
    }
}

internal static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>光标所在那块屏幕的工作区（物理像素）。</summary>
    public static RECT WorkAreaAt(int px, int py)
    {
        var pt = new Native.POINT { X = px, Y = py };
        var h = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (h != IntPtr.Zero && GetMonitorInfo(h, ref mi))
            return mi.rcWork;

        var wa = SystemParameters.WorkArea;
        return new RECT { Left = 0, Top = 0, Right = (int)wa.Right, Bottom = (int)wa.Bottom };
    }
}
