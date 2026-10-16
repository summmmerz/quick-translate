using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace QuickTranslate;

public partial class PopupWindow : Window
{
    private CancellationTokenSource? _cts;
    private string _sourceText = "";
    private int _anchorX, _anchorY;
    private DispatcherTimer? _copyTimer;
    private bool _positioning;

    public PopupWindow()
    {
        InitializeComponent();
        Deactivated += PopupWindow_Deactivated;
        SizeChanged += PopupWindow_SizeChanged;
    }

    /// <summary>
    /// 让弹窗永不激活（WS_EX_NOACTIVATE）。划词弹窗一旦抢走前台焦点，用户下一次划词时
    /// 目标程序要先夺回焦点，合成的 Ctrl+C 经常被丢掉，只能干等满 400ms 超时——
    /// 这就是"翻译完再划词会卡一下"的主因。
    /// 代价：卡片里的译文框拿不到键盘焦点，复制走「复制」按钮。
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
        Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE);
    }

    private void PopupWindow_Deactivated(object? sender, EventArgs e)
    {
        // NOACTIVATE 之后基本不会触发，留作兜底
        if (CardBorder.Visibility == Visibility.Visible) HideNow();
    }

    private void PopupWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 卡片高度是动态的，要重新定位；加互斥标志避免"移动→重排→再移动"自激
        if (_positioning || !IsVisible) return;
        if (CardBorder.Visibility != Visibility.Visible) return;
        _positioning = true;
        try { PositionWindow(_anchorX, _anchorY, isCard: true); }
        finally { _positioning = false; }
    }

    public void ShowDot(int px, int py, string source)
    {
        _sourceText = source;
        _anchorX = px;
        _anchorY = py;

        DotBorder.Visibility = Visibility.Visible;
        CardBorder.Visibility = Visibility.Collapsed;

        ShowActivated = false;
        PositionWindow(px, py, isCard: false);
        Show();
    }

    public void ShowTranslation(int px, int py, string source)
    {
        _sourceText = source;
        _anchorX = px;
        _anchorY = py;

        DotBorder.Visibility = Visibility.Collapsed;
        CardBorder.Visibility = Visibility.Visible;

        ShowActivated = false;
        PositionWindow(px, py, isCard: true);
        Show();

        StartTranslation();
    }

    public void ShowInfo(int px, int py, string message)
    {
        _sourceText = "";
        _anchorX = px;
        _anchorY = py;

        DotBorder.Visibility = Visibility.Collapsed;
        CardBorder.Visibility = Visibility.Visible;

        CardDictHead.Visibility = Visibility.Collapsed;
        CardOutputBox.Text = message;
        CardStatusText.Text = "";
        CardDirectionLabel.Text = "";

        ShowActivated = false;
        PositionWindow(px, py, isCard: true);
        Show();
    }

    public void HideNow()
    {
        _cts?.Cancel();
        Hide();
    }

    public bool Contains(int px, int py)
    {
        return WinUtil.Contains(this, px, py);
    }

    private void Dot_Click(object sender, MouseButtonEventArgs e)
    {
        // 圆点展开成卡片
        DotBorder.Visibility = Visibility.Collapsed;
        CardBorder.Visibility = Visibility.Visible;
        ShowActivated = false;

        StartTranslation();
    }

    private void CardClose_Click(object sender, RoutedEventArgs e)
    {
        HideNow();
    }

    private void CardCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CardOutputBox.Text))
        {
            Clipboard.SetText(CardOutputBox.Text);
            CardStatusText.Text = "已复制";

            _copyTimer?.Stop();
            _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyTimer.Tick += (s, ev) =>
            {
                _copyTimer.Stop();
                CardStatusText.Text = CardStatusText.Tag as string ?? "";
            };
            _copyTimer.Start();
        }
    }

    private async void StartTranslation()
    {
        if (string.IsNullOrEmpty(_sourceText)) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // 单个英文词先查离线词库，命中就不联网
        var local = Dict.Lookup(_sourceText);
        if (local != null)
        {
            ShowDict(local);
            return;
        }
        CardDictHead.Visibility = Visibility.Collapsed;

        CardOutputBox.Clear();
        CardStatusText.Text = "翻译中…";
        CardStatusText.Tag = null;

        CardDirectionLabel.Text = Translator.ShortName(Translator.DetectSource(_sourceText)) + " → " +
            Translator.ShortName(Translator.ResolveTarget(_sourceText, App.Cfg.TargetLang));

        var sw = Stopwatch.StartNew();
        try
        {
            string result = await Translator.TranslateAsync(_sourceText, App.Cfg, ct);
            if (ct.IsCancellationRequested) return;

            CardOutputBox.Text = result;
            string status = $"{sw.ElapsedMilliseconds} ms · {App.Cfg.Model}";
            CardStatusText.Text = status;
            CardStatusText.Tag = status;
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            CardOutputBox.Text = $"翻译失败：{ex.Message}";
            CardStatusText.Text = "";
            CardStatusText.Tag = null;
        }
    }

    /// <summary>渲染词库命中：卡片头上是音标/词性/考纲标签，正文是释义。</summary>
    private void ShowDict(DictEntry entry)
    {
        CardPhonetic.Text = string.IsNullOrWhiteSpace(entry.Phonetic) ? _sourceText : "/" + entry.Phonetic + "/";
        CardPos.Text = entry.Pos;
        CardTags.Text = Dict.TagText(entry.Tag);
        CardDictHead.Visibility = Visibility.Visible;

        CardDirectionLabel.Text = "词典";
        CardOutputBox.Text = entry.Translation;
        const string status = "ECDICT · 离线";
        CardStatusText.Text = status;
        CardStatusText.Tag = status;
    }

    private void PositionWindow(int px, int py, bool isCard)
    {
        var work = Monitors.WorkAreaAt(px, py);
        var (sx, sy) = WinUtil.Scale(this);

        double offsetX = isCard ? 14 : 10;
        double offsetY = isCard ? 18 : 12;

        // 默认右下
        double left = (px + offsetX) / sx;
        double top = (py + offsetY) / sy;

        // 计算窗口物理尺寸
        double winWidth = (isCard ? 360 : 18) * sx;
        double winHeight = (isCard ? Math.Min(ActualHeight, 360) : 18) * sy;

        // 右边放不下就改左边
        if (px + offsetX + winWidth > work.Right)
        {
            left = (px - offsetX - winWidth / sx) / sx;
        }

        // 下边放不下就往上
        if (py + offsetY + winHeight > work.Bottom)
        {
            top = (py - offsetY - winHeight / sy) / sy;
        }

        // 最终夹在工作区内
        if (left < work.Left / sx) left = work.Left / sx;
        if (top < work.Top / sy) top = work.Top / sy;
        if (left + winWidth / sx > work.Right / sx) left = work.Right / sx - winWidth / sx;
        if (top + winHeight / sy > work.Bottom / sy) top = work.Bottom / sy - winHeight / sy;

        Left = left;
        Top = top;
    }
}
