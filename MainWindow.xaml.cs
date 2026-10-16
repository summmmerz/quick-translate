using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace QuickTranslate;

public partial class MainWindow : Window
{
    // 折叠态 = 边框(2) + 标题栏(30) + 输入区(52 + 上下 margin 10)
    private const double IdleHeight = 94;
    // 展开态 = 折叠态 + ResultHost(126)
    private const double ResultHeight = 220;

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _copyTimer;

    public MainWindow()
    {
        InitializeComponent();
        LangList.ItemsSource = Languages.All;
        Loaded += MainWindow_Loaded;
        LocationChanged += MainWindow_LocationChanged;
        // InputBox 的 TextChanged 已在 XAML 里挂过，这里再挂会重复触发
        OutputBox.TextChanged += OutputBox_TextChanged;

        // 语言下拉用 StaysOpen=True（StaysOpen=False 会被触发它的那一次点击立刻关掉），
        // 所以关它得自己来：窗口失活、或者焦点回到输入框
        Deactivated += (_, _) => LangPopup.IsOpen = false;
        InputBox.GotFocus += (_, _) => LangPopup.IsOpen = false;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDirectionLabel();

        // 还原位置或默认到右下角
        var cfg = App.Cfg;
        if (cfg.WindowLeft.HasValue && cfg.WindowTop.HasValue)
        {
            Left = cfg.WindowLeft.Value;
            Top = cfg.WindowTop.Value;

            // 夹到屏幕内
            var (sx, sy) = WinUtil.Scale(this);
            int px = (int)(Left * sx);
            int py = (int)(Top * sy);
            var work = Monitors.WorkAreaAt(px, py);

            double right = Left + ActualWidth;
            double bottom = Top + ActualHeight;
            if (right * sx > work.Right) Left = work.Right / sx - ActualWidth;
            if (bottom * sy > work.Bottom) Top = work.Bottom / sy - ActualHeight;
            if (Left < work.Left / sx) Left = work.Left / sx;
            if (Top < work.Top / sy) Top = work.Top / sy;
        }
        else
        {
            // 贴主屏右下角
            var work = Monitors.WorkAreaAt((int)SystemParameters.PrimaryScreenWidth - 100,
                                           (int)SystemParameters.PrimaryScreenHeight - 100);
            var (sx, sy) = WinUtil.Scale(this);
            Left = work.Right / sx - ActualWidth - 18;
            Top = work.Bottom / sy - ActualHeight - 18;
        }
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        App.Cfg.WindowLeft = Left;
        App.Cfg.WindowTop = Top;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            StartTranslation();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            InputBox.Clear();
        }
    }

    private void InputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        InputPlaceholder.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        UpdateDirectionLabel();
    }

    private void OutputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        OutputPlaceholder.Visibility = string.IsNullOrEmpty(OutputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        InputBox.Clear();
        OutputBox.Clear();
        StatusText.Text = "";
        StatusText.Tag = null;
        SetResultVisible(false);
        DictHead.Visibility = Visibility.Collapsed;
        UpdateDirectionLabel();
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        LangPopup.IsOpen = !LangPopup.IsOpen;
    }

    private void LangPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string code }) return;
        LangPopup.IsOpen = false;
        if (App.Cfg.TargetLang == code) return;

        App.Cfg.TargetLang = code;
        App.Cfg.Save();
        UpdateDirectionLabel();

        // 译文已经在台面上、输入也还在，就按新语言重译一次
        if (ResultHost.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(InputBox.Text))
            StartTranslation();
    }

    /// <summary>标题栏那行字：空闲时只显示目标语言，有输入时显示「源 → 目标」。</summary>
    private void UpdateDirectionLabel()
    {
        if (DirectionLabel is null) return;
        string t = Translator.ShortName(Translator.ResolveTarget(InputBox.Text, App.Cfg.TargetLang));
        DirectionLabel.Text = (string.IsNullOrWhiteSpace(InputBox.Text)
            ? (App.Cfg.TargetLang == "auto" ? "自动" : t)
            : Translator.ShortName(Translator.DetectSource(InputBox.Text)) + " → " + t) + " ▾";
    }

    /// <summary>
    /// 折叠/展开译文区。切换时把窗口底边钉住（向上生长、向下收回），
    /// 所以贴屏幕底部的窗口不会因为展开而掉到屏幕外，反复翻译也不会累积漂移。
    /// </summary>
    private void SetResultVisible(bool show)
    {
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        if (ResultHost.Visibility == vis) return;

        bool placed = IsLoaded && !double.IsNaN(Top);
        double bottom = Top + ActualHeight;

        ResultHost.Visibility = vis;
        Height = show ? ResultHeight : IdleHeight;

        if (!placed) return;

        Top = bottom - Height;

        // 只在真的越出工作区时才夹回来（例如窗口被拖到屏幕顶部）
        var (sx, sy) = WinUtil.Scale(this);
        var work = Monitors.WorkAreaAt((int)((Left + Width / 2) * sx),
                                       (int)((Top + Height / 2) * sy));
        double topLimit = work.Top / sy;
        double bottomLimit = work.Bottom / sy;
        if (Top + Height > bottomLimit) Top = bottomLimit - Height;
        if (Top < topLimit) Top = topLimit;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.Cfg.Save();
        Application.Current.Shutdown();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputBox.Text))
        {
            Clipboard.SetText(OutputBox.Text);
            StatusText.Text = "已复制";

            _copyTimer?.Stop();
            _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyTimer.Tick += (s, ev) =>
            {
                _copyTimer.Stop();
                if (!string.IsNullOrEmpty(OutputBox.Text))
                {
                    // 恢复原状态（如果有翻译完成的状态信息）
                    StatusText.Text = StatusText.Tag as string ?? "";
                }
            };
            _copyTimer.Start();
        }
    }

    public void FocusInput()
    {
        Show();
        Activate();
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private async void StartTranslation()
    {
        string text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        OutputBox.Clear();
        StatusText.Tag = null;
        SetResultVisible(true);

        UpdateDirectionLabel();

        // 单个英文词先在离线词库里查：命中就不联网（更快，而且释义比大模型准），
        // 查不到、或者本来就是句子，才走 DeepSeek
        var local = Dict.Lookup(text);
        if (local != null)
        {
            ShowDict(text, local);
            return;
        }

        DictHead.Visibility = Visibility.Collapsed;
        StatusText.Text = "翻译中…";

        var sw = Stopwatch.StartNew();
        try
        {
            string result = await Translator.TranslateAsync(text, App.Cfg, ct);
            if (ct.IsCancellationRequested) return;

            OutputBox.Text = result;
            string status = $"{sw.ElapsedMilliseconds} ms · {App.Cfg.Model}";
            StatusText.Text = status;
            StatusText.Tag = status;
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            OutputBox.Text = $"翻译失败：{ex.Message}";
            StatusText.Text = "";
            StatusText.Tag = null;
        }
    }

    /// <summary>渲染词库命中：头上是音标/词性/考纲标签，正文走 OutputBox（「复制」照旧可用）。</summary>
    private void ShowDict(string word, DictEntry entry)
    {
        DictPhonetic.Text = string.IsNullOrWhiteSpace(entry.Phonetic) ? word : "/" + entry.Phonetic + "/";
        DictPos.Text = entry.Pos;
        DictTags.Text = Dict.TagText(entry.Tag);
        DictHead.Visibility = Visibility.Visible;

        OutputBox.Text = entry.Translation;
        const string status = "ECDICT · 离线";
        StatusText.Text = status;
        StatusText.Tag = status;
    }

    public void SetStatusText(string text)
    {
        StatusText.Text = text;
    }
}
