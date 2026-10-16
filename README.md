# 轻译 / QuickTranslate

Windows 划词翻译小工具。**C# + WPF（`net10.0-windows`），零 NuGet 依赖**，只用 BCL + WPF + P/Invoke。

深色界面，强调色为曼城蓝 `#6CABDD`。常驻一个迷你窗，选中文字即出翻译；单个英文单词优先查本地离线词典，不联网。

## 功能

- **划词翻译**：鼠标拖选 / 双击选词后，在光标处弹出一个圆点，点击展开结果卡片。
  （不用每次单击都发 `Ctrl+C`——只在判定为"选择动作"时才取词：位移 > 4px 的拖选，或 450ms 内的双击。）
- **热键翻译**：默认 `Alt+Q` 翻译当前选中的文字，`Alt+W` 聚焦常驻窗输入框。
- **离线英汉词典**：单个英文单词先查本地 `dict.bin`（ECDICT 最常用档，37,234 词条 + 28,504 条变形指向），
  命中则显示音标 / 词性 / 考纲标签 / 释义，**不联网**；未命中或非单词才走在线翻译。
- **自动判断方向**：`auto` 模式下中文 → 英，其余 → 中；也可在配置里指定目标语言
  （`auto/zh/en/ja/ko/fr/de/ru/es`）。
- 取词走后台 STA 线程并自动备份 / 还原剪贴板，不打断你正在复制的内容。

## 构建与运行

需要 .NET 10 SDK。

```powershell
dotnet build -c Release              # 零错误零警告
dotnet run   -c Release              # 弹出常驻窗
dotnet publish -c Release            # 产物：bin\Release\net10.0-windows\QuickTranslate.exe
```

`dict.bin` 必须与 exe 同目录（`.csproj` 已配 `<Content>` 自动拷贝），因此 `dotnet run` 可直接跑。

### 无 GUI 自检

不建窗口、不装钩子，直接跑通「读配置 → 请求翻译 → 解析」整条链路：

```powershell
dotnet run -c Release -- --selftest
```

结果写入 `%TEMP%\quicktranslate-selftest.txt`，格式 `OK <译文>` 或 `FAIL <异常信息>`。

## 配置

首次运行后编辑 `%APPDATA%\QuickTranslate\config.json`：

```json
{
  "BaseUrl": "https://api.deepseek.com/chat/completions",
  "Model": "deepseek-chat",
  "ApiKey": "sk-...",
  "HotkeySelection": "Alt+Q",
  "HotkeyInput": "Alt+W",
  "TargetLang": "auto"
}
```

`BaseUrl` 是任意 OpenAI 兼容的 chat completions 端点，换成别的服务商也可以。
若 `ApiKey` 为空，程序会尝试从 [Pot](https://github.com/pot-app/pot-desktop) 的配置里取一个已配置的 key。

窗口位置（`WindowLeft` / `WindowTop`）由程序自己写回，退出时统一落盘。

## 离线词库

`dict.bin` 由 [skywind3000/ECDICT](https://github.com/skywind3000/ECDICT)（MIT）编译而来，
只取最常用一档：COCA 词频 `frq<=30000`，或带考纲标签（zk/gk/cet4/cet6/ky/toefl/ielts/gre）、
牛津、柯林斯 ≥2 星。

格式为自研的 **QDIC**：`'QDIC'` 头 20 B + 定长索引 12 B/条 + 键区 + 数据区，
用 `MemoryMappedFile` 按页换入（词库多大都不占常驻内存），查询对索引段做二分，约 16 次探测、亚毫秒级。

重新生成：

```powershell
python tools\BuildDict.py <ecdict.csv> core    # 档位：core | wide | full
```

| 档位 | 体积 | 词条 |
|---|---|---|
| core（默认，已随仓库提供） | 4.44 MB | 37,234 |
| wide | 5.76 MB | 59,111 |
| full | 51.2 MB | 759,973 |

## 许可

本项目代码基于 MIT 许可。离线词典数据来自 ECDICT（MIT）。
