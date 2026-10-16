# 轻译 / QuickTranslate

Windows 划词翻译小工具。C# + WPF，`net10.0-windows`，**零 NuGet 依赖**，目标产物是一个单文件 exe。

本文件同时是给 AI 协作者的工程说明，请先读完再动手。

---

## 1. 硬约束

- 不许引入任何 NuGet 包 / 第三方库。只用 BCL + WPF + P/Invoke。
- 不许改动 `D:\1\quick-translate` 之外的任何路径。
- 不要 `git commit`、不要 push。
- 不要 `dotnet run` 启动 GUI（会挂住）。验证走下面的 `--selftest`。
- 界面文案一律中文。深色主题，强调色曼城蓝 `#6CABDD`。
- 保持轻量：不写历史记录、不多引擎、不写托盘、不写设置面板。配置直接改 `%APPDATA%\QuickTranslate\config.json`。

## 2. 已完成（**不要重写**，只在编译报错时做最小修正）

| 文件 | 公共接口 |
|---|---|
| `QuickTranslate.csproj` | WinExe、UseWPF、net10.0-windows、ImplicitUsings、Nullable |
| `Config.cs` | `class Config`：属性 `BaseUrl`(默认为 deepseek chat 接口)、`Model`、`ApiKey`、`HotkeySelection`="Alt+Q"、`HotkeyInput`="Alt+W"、`TargetLang`="auto"（目标语言代码）、`WindowLeft?`、`WindowTop?`；同文件另有 `class Lang{Code,Name,Prompt}` 和 `static class Languages.All`（`auto/zh/en/ja/ko/fr/de/ru/es`）；`static Config Load()`、`void Save()`、`bool TryImportKeyFromPot()`（首次运行从 Pot 配置捡 key）；`static string Dir` / `FilePath` = `%APPDATA%\QuickTranslate\config.json` |
| `Translator.cs` | `static string DetectSource(string)` → `zh`/`ja`/`ko`/`ru`/`en`（假名优先判日文，再谚文、汉字、西里尔，否则英文）；`static string ResolveTarget(string text, string? targetLang)`（`auto` 时中文→英、其余→中）；`static string ShortName(string code)`（中/英/日…）；`static Task<string> TranslateAsync(string text, Config cfg, CancellationToken ct = default)`，失败时抛 `Exception`，消息形如 `HTTP 401 ...` |
| `Interop.cs` | `internal static class Native`：`WM_HOTKEY`、`WM_LBUTTONDOWN/UP`、`MOD_ALT/CONTROL/SHIFT/NOREPEAT`、`VK_ESCAPE`、`GetClipboardSequenceNumber()`、`SendCtrlC()`、`POINT` 等<br>`internal sealed class MouseHook : IDisposable`：`event Action<int,int> ButtonDown / ButtonUp`（参数是**物理像素**坐标，回调已在 UI 线程）、`bool Install()`、`Dispose()`<br>`internal static class Clip`：`static string? GrabSelection()`（备份剪贴板 → 模拟 Ctrl+C → 取回 → 还原，最长阻塞约 400ms） |
| `ClipService.cs` | `new ClipService()`（后台 STA 线程）；`void Grab(Action<string?> onResult)`，**回调在 UI 线程**，取不到就是 `null`。**划词取词必须走它**，绝不能在 UI 线程直接调 `Clip.GrabSelection()` |
| `Hotkeys.cs` | `new Hotkeys(Window)`（要求窗口已 Show，句柄就绪）；`bool Register(string spec, Action)`；`int RegisterTemp(string spec, Action)`；`void Unregister(int id)`。spec 形如 `"Alt+Q"`、`"Ctrl+Shift+T"` |
| `WinUtil.cs` | `WinUtil.Contains(Window, int px, int py)`（物理像素点是否在窗口内，已处理 DPI）、`WinUtil.ToDip(Window, px, py)`、`WinUtil.Scale(Visual)`<br>`Monitors.WorkAreaAt(int px, int py)` 返回物理像素的 `RECT{Left,Top,Right,Bottom}` |
| `Theme.xaml` | 画刷 `Bg`(#0E1116) `Panel` `Line` `Fg` `Muted` `Faint` `Accent`(#6CABDD) `AccentDim`；样式 `IconBtn`(22×22 标题栏图标钮) `MiniBtn`(底部文字钮) `Editor`(输入 TextBox) `Reader`(只读可选中 TextBox) |
| `App.xaml` | `ShutdownMode="OnExplicitShutdown"`，已合并 `Theme.xaml`。**不要改** |

## 3. 实现蓝图（**已全部实现**，下面是当初的设计意图）

> ⚠️ 本节细节已过时（例如窗口尺寸 336×396、外发光、`KeyDown`），**只剩公共方法签名和交互状态机还算数**。外观、布局、尺寸一律以 `MainWindow.xaml` / `PopupWindow.xaml` / `Theme.xaml` 现码为准，改代码前先读源码。

### 3.1 `App.xaml.cs`（`partial class App : Application`）

- `public static Config Cfg`（供窗口读取）。
- `OnStartup`：
  1. 单实例保护：`Mutex` 抢 `"QuickTranslate_SingleInstance"`，抢不到就弹 `MessageBox`「轻译已在运行」并 `Shutdown()`。
  2. `Cfg = Config.Load(); Cfg.TryImportKeyFromPot();`
  3. `new MainWindow().Show()`（先 Show，热键要用它的句柄）。
  4. `new PopupWindow()`（只创建，不 Show）。
  5. `new Hotkeys(mainWindow)`，注册 `Cfg.HotkeySelection` → 划词翻译流程，`Cfg.HotkeyInput` → `mainWindow.FocusInput()`。注册失败（返回值 false）要在常驻窗状态栏提示热键被占用。
  6. `new ClipService()`。
  7. `new MouseHook()`，接 `ButtonDown` / `ButtonUp`，`Install()`。
- 交互状态机（全部在 UI 线程，取词走 `ClipService.Grab`）：
  - `ButtonDown(x,y)`：记录按下点；若弹窗可见且该点**不在**弹窗内、也不在常驻窗内 → 关掉弹窗。
  - `ButtonUp(x,y)`：若该点在常驻窗或弹窗内 → 忽略。否则判断"这次是不是选择动作"：位移平方 > 20（拖选）**或** 与上一次 mouseup 间隔 < 450ms 且距离 < 6px（双击选词）。不满足就什么也不做（避免每次单击都去发 Ctrl+C）。满足则 `ClipService.Grab(text => text 非空且长度 ≤ 3000 时 popup.ShowDot(x, y, text))`。
  - 热键划词：`ClipService.Grab(...)` → 取到文本就 `popup.ShowTranslation(cursorX, cursorY, text)`，取不到就 `popup.ShowInfo(x, y, "没有选中任何文字")`，坐标用 `Native.GetCursorPos`。
- 弹窗 `IsVisibleChanged`：可见时 `RegisterTemp("Escape", popup.HideNow)`，隐藏时 `Unregister`（Esc 只在弹窗可见时抢占）。
- `OnExit`：撤销钩子、注销热键、`Cfg.Save()`。

### 3.2 `MainWindow.xaml` / `.cs`（常驻迷你窗）

外观：`WindowStyle=None`、`AllowsTransparency=True`、`Background=Transparent`、`Topmost=True`、`ShowInTaskbar=False`、`ResizeMode=NoResize`、`WindowStartupLocation=Manual`，宽 336 高 396，字体 `Microsoft YaHei UI, Segoe UI`。根 `Border` `CornerRadius=14`，`Background={StaticResource Bg}`，1px `Line` 描边，外发光 `DropShadowEffect`。

布局（从上到下）：
1. 高 38 的标题栏，整条可拖动（`MouseLeftButtonDown` → `DragMove()`）：左边 7px 的 `Accent` 圆点 + 文字「轻译」+ 灰色方向标签（初始「自动」，翻译时显示「中 → 英」或「英 → 中」）；右边两个 `IconBtn`：「⌫」清空、「✕」退出程序。
2. 高 86 的输入区：`TextBox` 用 `Editor` 样式，`AcceptsReturn=True`；叠一个灰色的占位文字「输入或粘贴文字，Enter 翻译」，有输入时隐藏。
3. 1px 分隔线。
4. 剩余高度：只读 `TextBox`（`Reader` 样式）显示译文，空白时叠一个 `Faint` 色的「译文显示在这里」。
5. 高 32 的底栏：左边灰色状态文字，右边 `MiniBtn`「复制」。

行为：
- `Loaded`：若 `Cfg.WindowLeft/Top` 有值就还原（并用 `Monitors.WorkAreaAt` 夹到屏幕内），否则贴主屏工作区右下角，距边 18。
- `LocationChanged` → 把 `Left/Top` 存回 `Cfg`（不用每次写盘，退出时统一 `Save()`）。
- 输入框 `KeyDown`：`Enter`（不带 `Shift`）→ 翻译；`Esc` → 清空输入。
- `Func`：`public void FocusInput()` → `Show()` + `Activate()` + 聚焦输入框并全选。
- 翻译：清空译文、状态显示「翻译中…」、方向标签按 `Translator.TargetLanguage` 更新；成功后在底栏显示 `"{耗时} ms · {Model}"`；异常时译文位置显示「翻译失败：{ex.Message}」。重复触发要取消上一次（`CancellationTokenSource`）。
- 「复制」把译文写进剪贴板，底栏短暂显示「已复制」再用 `DispatcherTimer` 恢复。
- 「✕」→ `Cfg.Save()` + `Application.Current.Shutdown()`。

### 3.3 `PopupWindow.xaml` / `.cs`（划词圆点 + 结果卡片）

一个窗口两种形态，靠两个 `Border` 的可见性切换，`SizeToContent=WidthAndHeight`、`Topmost`、`ShowInTaskbar=False`、`WindowStyle=None`、`AllowsTransparency=True`。

- **圆点态**：`Ellipse` 或 `CornerRadius=9` 的 18×18 圆，填充 `Accent`，1px 深色描边，`Cursor=Hand`，点击后切卡片态。
- **卡片态**：宽 360，`MaxHeight=360`，圆角 12，`Bg` 底 + `Line` 描边 + 外发光。内部三行：① 高 30 的头（小圆点 + 灰色方向标签 + 右侧 `IconBtn`「✕」）；② 可滚动的只读 `Reader` 文本框显示译文；③ 高 30 的底栏（左灰色状态，如「翻译中…」/「420 ms · deepseek-chat」，右 `MiniBtn`「复制」）。

公共方法（签名固定，App 会照着调）：
- `void ShowDot(int px, int py, string source)` —— 记录锚点和原文，显示圆点，**不抢焦点**（`ShowActivated=false`，只 `Show()`，不要 `Activate()`）。
- `void ShowTranslation(int px, int py, string source)` —— 显示卡片并立刻翻译。
- `void ShowInfo(int px, int py, string message)` —— 显示卡片但只显示提示文字，不调接口。
- `void HideNow()`
- `bool Contains(int px, int py)`（物理像素，转调 `WinUtil.Contains`）

定位（坐标都是物理像素）：用 `Monitors.WorkAreaAt` 拿当前屏幕工作区；圆点默认放在光标右下 `(+10,+12)`，越界就翻到左上。卡片默认 `(+14,+18)`，若右边放不下就改成放在光标左侧，下边放不下就往上挪，最终夹在工作区内。`Left/Top` 是 DPI 无关单位，用 `WinUtil.ToDip` 换算；因为卡片高度要等布局才知道，`SizeChanged` 时按当前 `ActualWidth/ActualHeight` 重新定位一次。

焦点与关闭：点圆点展开卡片时 `Activate()`（这样卡片失焦能自动关：「失焦」→ `HideNow()`，但圆点态不要接这个）。`HideNow()` 要取消进行中的翻译请求。卡片态 `Activate()` 后按 Esc 由 App 注册的临时热键处理，窗口本身不用管 Esc。

## 4. 验收

1. `dotnet build -c Release` **零错误零警告**（警告也要清掉）。
2. `App.OnStartup` 开头加一个自检分支：命令行参数含 `--selftest` 时，不做任何 UI（不建窗口、不装钩子），直接读配置、`Translator.TranslateAsync("Good morning, have a nice day!")`，把结果写进 `%TEMP%\quicktranslate-selftest.txt`（格式 `OK <译文>` 或 `FAIL <异常信息>`），然后 `Shutdown()`。这样可以在没有 GUI 的情况下验证 key / 网络 / 解析整条链路。
3. 自检命令：`dotnet run -c Release -- --selftest`（30 秒内必须自己退出）。

## 5. 运行方式

`dotnet run -c Release`（会弹常驻窗），或 `dotnet publish -c Release` 后跑 `bin\Release\net10.0-windows\QuickTranslate.exe`。

## 6. 离线词库（ECDICT）

数据来自 [skywind3000/ECDICT](https://github.com/skywind3000/ECDICT)（MIT）。只取最常用一档：
COCA 词频 `frq<=30000` 或带考纲标签（zk/gk/cet4/cet6/ky/toefl/ielts/gre）/牛津/柯林斯≥2 星，
得 37,234 词条 + 28,504 条变形指向（went→go、children→child、running→run…），
编译成 **`dict.bin` 4.44 MB**（QDIC：`'QDIC'` 头 20 B + 定长索引 12 B/条 + 键区 + 数据区，
MemoryMappedFile + 二分查找，零 NuGet）。

- 重新生成：`python tools\BuildDict.py <ecdict.csv> core`（档位 `core|wide|full`，wide 5.76 MB/59,111 词，full 51.2 MB/759,973 词）。
- `dict.bin` 必须与 exe 同目录（csproj 已 `<Content Include="dict.bin" CopyToOutputDirectory="PreserveNewest" />`）。
- 命中规则：`Dict.IsSingleWord` 且查到 → 显示音标/词性/考纲标签/释义，**不联网**，状态写 `ECDICT · 离线`；
  查不到或不是单个词 → 走 DeepSeek。
- 两个入口都要处理：`MainWindow.ShowDict(string, DictEntry)`、`PopupWindow.ShowDict(DictEntry)`；
  切回 AI 结果时都要把 `DictHead`/`CardDictHead` 设回 `Collapsed`。
