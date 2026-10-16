using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace QuickTranslate;

/// <summary>
/// 划词取词放到独立的 STA 线程上做：它会阻塞 300~400ms 等剪贴板，
/// 绝不能在 UI 线程上跑——UI 线程一旦卡住，鼠标钩子的回调就排不上队，
/// 轻则鼠标发滞，重则被 Windows 静默摘掉钩子。
/// </summary>
internal sealed class ClipService
{
    private readonly Dispatcher _worker;

    public ClipService()
    {
        var ready = new ManualResetEventSlim(false);
        Dispatcher? d = null;
        var t = new Thread(() =>
        {
            d = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "clip-grab" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        ready.Wait(2000);
        _worker = d ?? Dispatcher.CurrentDispatcher;
    }

    public void Grab(Action<string?> onResult)
    {
        _worker.BeginInvoke(new Action(() =>
        {
            string? text = null;
            try { text = Clip.GrabSelection(); } catch { }
            var ui = Application.Current?.Dispatcher;
            if (ui is null) return;
            ui.BeginInvoke(new Action(() => onResult(text)));
        }));
    }
}
