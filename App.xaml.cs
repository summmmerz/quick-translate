using System.IO;
using System.Threading;
using System.Windows;

namespace QuickTranslate;

public partial class App : Application
{
    public static Config Cfg = null!;

    private Mutex? _mutex;
    private MainWindow? _mainWindow;
    private PopupWindow? _popupWindow;
    private Hotkeys? _hotkeys;
    private ClipService? _clipService;
    private MouseHook? _mouseHook;
    private int _escHotkeyId;

    // 鼠标状态机
    private int _lastDownX, _lastDownY;
    private int _lastUpX, _lastUpY;
    private DateTime _lastUpTime = DateTime.MinValue;
    private bool _lastWasClick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 自检分支
        if (e.Args.Contains("--selftest"))
        {
            RunSelfTest();
            Shutdown();
            return;
        }

        // 单实例保护
        _mutex = new Mutex(true, "QuickTranslate_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("轻译已在运行", "轻译", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 加载配置
        Cfg = Config.Load();
        Cfg.TryImportKeyFromPot();

        // 创建主窗口
        _mainWindow = new MainWindow();
        _mainWindow.Show();

        // 创建弹窗（不显示）
        _popupWindow = new PopupWindow();

        // 注册热键
        _hotkeys = new Hotkeys(_mainWindow);

        bool selOk = _hotkeys.Register(Cfg.HotkeySelection, OnHotkeySelection);
        bool inpOk = _hotkeys.Register(Cfg.HotkeyInput, () => _mainWindow.FocusInput());

        if (!selOk || !inpOk)
        {
            string msg = "";
            if (!selOk) msg += $"划词翻译热键 {Cfg.HotkeySelection} 被占用\n";
            if (!inpOk) msg += $"输入热键 {Cfg.HotkeyInput} 被占用";
            _mainWindow.SetStatusText(msg.Trim());
        }

        // 剪贴板服务
        _clipService = new ClipService();

        // 鼠标钩子
        _mouseHook = new MouseHook();
        _mouseHook.ButtonDown += OnMouseButtonDown;
        _mouseHook.ButtonUp += OnMouseButtonUp;
        _mouseHook.Install();

        // 弹窗 Esc 热键
        _popupWindow.IsVisibleChanged += (s, ev) =>
        {
            if (_popupWindow.IsVisible)
            {
                _escHotkeyId = _hotkeys.RegisterTemp("Escape", () => _popupWindow.HideNow());
            }
            else
            {
                if (_escHotkeyId != 0)
                {
                    _hotkeys.Unregister(_escHotkeyId);
                    _escHotkeyId = 0;
                }
            }
        };
    }

    private void OnMouseButtonDown(int x, int y)
    {
        _lastDownX = x;
        _lastDownY = y;

        // 点击窗口外关闭弹窗
        if (_popupWindow is not null && _popupWindow.IsVisible)
        {
            bool inPopup = _popupWindow.Contains(x, y);
            bool inMain = _mainWindow is not null && WinUtil.Contains(_mainWindow, x, y);
            if (!inPopup && !inMain)
            {
                _popupWindow.HideNow();
            }
        }
    }

    private void OnMouseButtonUp(int x, int y)
    {
        // 点在窗口内直接忽略
        bool inMain = _mainWindow is not null && WinUtil.Contains(_mainWindow, x, y);
        bool inPopup = _popupWindow is not null && _popupWindow.Contains(x, y);
        if (inMain || inPopup) return;

        // 判断是否选择动作
        int dx = x - _lastDownX, dy = y - _lastDownY;
        bool isDrag = dx * dx + dy * dy > 20;

        int dx2 = x - _lastUpX, dy2 = y - _lastUpY;
        var now = DateTime.Now;
        double gap = (now - _lastUpTime).TotalMilliseconds;
        // 只有"上一次也是单击"才算双击：否则拖选完之后随手点一下也会被当成双击，
        // 白送一次 Ctrl+C（既慢，又会把别处的剪贴板搅乱）
        bool isDoubleClick = _lastWasClick && gap < 450 && dx2 * dx2 + dy2 * dy2 < 36;

        _lastUpX = x;
        _lastUpY = y;
        _lastUpTime = now;
        _lastWasClick = !isDrag;

        if (!isDrag && !isDoubleClick) return;

        // 取词
        _clipService?.Grab(text =>
        {
            if (!string.IsNullOrEmpty(text) && text.Length <= 3000)
            {
                _popupWindow?.ShowDot(x, y, text);
            }
        });
    }

    private void OnHotkeySelection()
    {
        Native.GetCursorPos(out var pt);
        _clipService?.Grab(text =>
        {
            if (!string.IsNullOrEmpty(text) && text.Length <= 3000)
            {
                _popupWindow?.ShowTranslation(pt.X, pt.Y, text);
            }
            else
            {
                _popupWindow?.ShowInfo(pt.X, pt.Y, "没有选中任何文字");
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mouseHook?.Dispose();
        _hotkeys?.Dispose();
        Cfg?.Save();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void RunSelfTest()
    {
        string result;
        try
        {
            var cfg = Config.Load();
            if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                result = "FAIL 配置中缺少 ApiKey";
            }
            else
            {
                var task = Translator.TranslateAsync("Good morning, have a nice day!", cfg);
                if (!task.Wait(30000))
                {
                    result = "FAIL 翻译超时（30秒）";
                }
                else
                {
                    var entry = Dict.Lookup("running");
                    result = "OK " + task.Result + " | dict=" +
                        (entry == null ? "MISS" : entry.Translation.Replace("\n", "|"));
                }
            }
        }
        catch (Exception ex)
        {
            result = "FAIL " + ex.Message;
        }

        string path = Path.Combine(Path.GetTempPath(), "quicktranslate-selftest.txt");
        File.WriteAllText(path, result);
    }
}
