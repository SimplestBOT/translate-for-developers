# 架构盘点与模块边界（阶段 0）

> 目标：**不是重构**，而是给后续迁移画出模块地图，避免隐式依赖和重复迁移。
> 规则：任何新能力只允许落到「最终归属」指定的技术栈；**AHK 不再新增核心业务能力**，只做兼容性暴露。
> 本文档随迁移推进更新（每完成一个阶段勾掉对应条目）。

## 最终架构（North Star）

```
React + TypeScript (UI)
        ↓  Protocol（消息契约，长期稳定）
C# Host（最终宿主）
        ├── Translation Core
        ├── Provider (MyMemory / 百度 / 未来更多)
        ├── Configuration（config.conf 唯一 Owner）
        ├── Clipboard
        ├── Hotkey
        ├── Tray
        ├── Window (WebView2 宿主)
        └── Infrastructure (HTTP / JSON / Logging)
AHK = 0（阶段 6 ✅）；Named Pipe 桥 = 0（阶段 7 ✅）——迁移收官
```

迁移总路线：**阶段 0 盘点 → 1 C# Host+WebView2 → 2 Protocol/Transport 分层 → 3 C# Translation Core/Provider/Configuration/Clipboard → 4 React UI 按页渐进接管 → 5 C# Hotkey/Tray/Window → 6 删 AHK → 7 Legacy 清理/测试完善/发布**。

- [x] 阶段 0 盘点（2026-09-01）
- [x] 阶段 1 C# Host+WebView2（2026-09-01）
- [x] 阶段 2 Protocol/Transport 分层（2026-09-01）
- [x] **阶段 3 C# 业务核心（2026-09-01）**：`csharp\TranslatorCore` 类库（Translation/Providers/Configuration/Clipboard/Infrastructure）；宿主承接 translate/copy/config_set；config.conf 唯一写方移交 C#（AHK 降只读，宿主不在场时兜底直写）；Core 自测 56 断言 + 端到端验证（真实翻译/testError 错误注入/config_set 落盘）
- [x] 阶段 4 React UI 按页渐进（**4a settings ✅ 2026-09-01**：`webui\`（Vite+React19+TS）Settings 页双轨上线——`Wv2Html()` 优先 `webui\dist\`、`TFD_LEGACY_UI=1` 回退 legacy html；协议层 `webui\src\bridge\`（post/on/installBridge）；组件化（LangSelect/ProviderPicker/BaiduKeysCard/HotkeyCard，为设置集中化预留）；宿主 push 加固：NavigationCompleted 门闩 + PostWebMessageAsJson 主通道 + `__recv` 注入兜底（legacy 页继续消费）；`settings ui_event` 自动化锚点。**4b result ✅ 2026-09-01**：React 结果页双轨上线（三态：loading 骨架/unblur 译文/错误卡+重试；复制 ✓ 反馈、Esc/Enter/Ctrl+C、字符数与 elapsedMs 徽标、provider 点颜色，行为逐项对齐 legacy）；宿主承接 result 页 translate/copy（阶段 3 已有）；`result ui_event` 自动化锚点；修复宿主双通道推送下 React 页重复消费 init 的问题（protocol.ts 键序无关信封去重，见 known-issues）。**4c capture ✅ 2026-09-01**：React 捕获窗双轨上线（capturing 点阵/captured 键帽 stagger 弹出/应用·重按·取消；Esc 归捕获语义不挂页面关窗，对齐 legacy `__noEsc`）；`capture ui_event` 锚点。**4d config ✅ 2026-09-01**：React 百度密钥配置窗双轨上线（步骤卡+openurl 链接+密钥表单 显示/隐藏·抖动·Enter 提交；saveBaidu/openurl 仍走 AHK handler）；`config ui_event` 锚点。**阶段 4 全部完成；Hotkey/托盘等窗口集成仍在 AHK（阶段 5 迁移）**）
- [~] 阶段 5 C# Hotkey/Tray/Window/选中捕获（**5a ✅ 2026-09-01**：① DPI——宿主 app.manifest PerMonitorV2 + MainWindow 尺寸按屏 DPI 缩放（修复遗留问题 1；AHK 本地 WvWindow 降级路径仍 legacy 冻结）；② 热键捕获——宿主 `Hotkey/CaptureManager`（WH_KEYBOARD_LL+WH_MOUSE_LL 钩子状态机：单例会话/30s 超时/窗口关闭即销毁/捕获期拦截当前热键与侧键）承接 capture/settings 页捕获消息流（修复遗留问题 2，AHK CaptureRound 跨轮残留缺陷不复刻）；应用 = ConfigStore 写盘 + C→A `hotkey_changed` → AHK 重注册翻译热键（C 写盘/A 注册切分）；协议 minor 1→2。**5b ✅ 2026-09-01：宿主独立常驻**——`Standalone.cs`（TrayController：托盘图标/菜单【立即翻译/语言双子菜单/提供商/设置·热键·密钥入口/退出】+ RegisterHotKey 全局翻译热键【AHK 串解析】；SelectionCapture：剪贴板全格式备份→Ctrl+C→轮询→恢复）；宿主**自开窗**（磁盘 HTML 解析 webui\dist→legacy html，四页 init 由 C# 组装【settings langs 表迁入 LanguageTable】，result 携选中文本直推）；**双模式**：启动即独立（托盘+热键），AHK hello 接入让渡（托盘/热键归 AHK、窗口/页面消息仍宿主），管道断开回归独立并重听管道（宿主常驻不退出，仅托盘退出结束）；设置业务（setLang/setProvider/saveBaidu/openurl）自开窗由宿主直处理、AHK 窗口沿用 AHK 业务流。调试：`--open <page>[,text]`、`TFD_HEADLESS=1`、`TFD_CONFIG`。e2e：独立模式四页 self-init+渲染锚点+真实翻译全绿；兼容模式让渡/断开/回归+热键重注册全绿。**5c ✅ 2026-09-01：托盘菜单逐项对齐 AHK（设置…=默认项双击/当前热键信息行/提供商整行点击切换（ToggleProvider 语义）/语言子菜单全表定义序勾选/补「配置百度密钥…」行/退出；气泡=ShowToast 等价）；ConfigStore 补 hotkey.conf 兼容读取（config 缺失时仅热键回退，对齐 AHK LoadConfig）；启动入口切换 start-translator.bat → 宿主 exe（bat 注释保留 AHK 回退；双实例禁止并存——热键冲突气泡引导）；启动/热键失败气泡。沙箱回归：四页锚点+兼容往返+hotkey.conf 回退全绿；托盘交互细节需桌面实测。阶段 5 全部完成；**阶段 6（删 AHK）可启动**——前置：宿主托盘/热键/选中捕获桌面实测通过后，AHK 侧仅余 -preview 本地调试与宿主缺失降级路径）**）
- [x] 阶段 6 删 AHK（2026-09-02）：`scripts\translator.ahk`/`ui.ahk`/`bridge.ahk`/`lib\`/`ml-test*.ahk`/`start-translate.bat` 退役（备份于 `backup-stage6-ahk\`）；宿主删 AHK 兼容模式——EnterAhkMode 让渡、page_event 透传、hotkey_* 桥五消息（capture/apply/cancel/changed/ack）、断开回归逻辑全删，纯独立模式（托盘/热键/捕获/窗口全宿主）；协议 minor 2→3（v1.3）；桥降级为测试面（hello/open_window/push/config_set/close_window 保留供 e2e-synth，阶段 7 删）；`config.conf` 唯一写方=宿主，hotkey.conf 兼容读取保留；e2e-synth 修复三处自身缺陷（Q 函数缺失/测试页 payload 违反协议/等待窗过短+剪贴板残留短路）并将 configPath 改指 %TEMP% 测试文件（不再触碰真实密钥配置）；start-translator.bat/README/scripts\README 同步。回归：Core 56 断言 + --selftest + 五页锚点 + REUSE 复用 + e2e-synth 7/7 全绿
- [x] 阶段 7 Legacy 清理/测试完善/发布（2026-09-02）：①键帽显示 bug 修复——FormatHotkey/KeysJson 的字符类 `[\x5E\x21\x2B\x23]` 因 `\x` 贪心+类内未转义 `^` 编译成**否定字符类**（剥掉主键留下 "!"），改为 `[\^!+#]`，`^!H` 键帽恢复 Ctrl+Alt+H（CDP 终验）；②Named Pipe 桥测试面整体删除——`Bridge\` 两文件、Program.cs 桥消息处理（OnLine/HandleMessage/HandleHello/HandleOpenWindow/HandlePush/HandleConfigSet/SendReady/OnDisconnected/Extract* 助手）、PageBusiness.ObservePush/ApplyConfigSet/testError 死代码、Protocol 桥常量与 Envelope；协议 v1.4（minor 4）；SelfTest 删管道项；`TFD_PIPE_NAME` 降级为 Mutex 隔离键；③scripts 清理——legacy html\、.webview2\、WebView2Loader.dll、e2e-synth.ahk、synth-probe、cdp-eval.mjs、全部排障探针与临时日志移入 `backup-stage7-legacy\`；根目录 translator.exe/translator.lnk/bin\（AHK 运行时）与含密钥遗留副本 config.conf 移入备份；④webui——test 协议诊断页删除（五页→四页）、版本 v1.5；⑤文档终版（protocol.md 删 §6、README/scripts\README 重写）+ release src\ 镜像同步。回归：build 0 错 + Core 56 断言 + selftest + 四页锚点 + REUSE + 真实划词链路。**全迁移收官：最终架构=React 四页 + C# 宿主（WinForms/WebView2/托盘/热键/捕获）+ TranslatorCore，AHK=0、桥=0**

## 模块清单与最终归属

### Core → 全部归 C#（阶段 3）

| 模块 | 现有实现（AHK 函数 / 文件） | C# 落点 | 阶段 |
|---|---|---|---|
| Translation | `TranslateText` `TranslateSafe` `DoTranslateWorker`、分片 `SplitTextByChars` `SplitTextByBytes` | `Translator.Core.Translation` | **3 ✅** |
| Provider | `TranslateMyMemory` `TranslateBaidu`、签名 `Md5Hex`、`UrlEncode`、`ProviderName` | `Translator.Core.Providers`（`ITranslationProvider`） | **3 ✅** |
| OCR（优化 5，v1.6） | 无（新增能力：截图 → `Windows.Media.Ocr` 识别） | `Translator.Core.Ocr`（`OcrService`/`OcrText` 纯函数；net48 经 Windows.winmd + GAC System.Runtime.WindowsRuntime，运行时零部署） | **优化 5 ✅** |
| Configuration | `SaveConfig` `LoadConfig`、`config.conf` / `hotkey.conf` 兼容读取 | `Translator.Core.Configuration`（唯一 Owner 移交） | **3 ✅**（写方移交；hotkey.conf 兼容读取随阶段 5 热键迁移） |

### Windows Integration → C#（分批）

| 模块 | 现有实现 | C# 落点 | 阶段 |
|---|---|---|---|
| Window | `WvWindow` 壳、`DwmRound` `WorkAreaAt`、NearCursor 定位、`DragWindow` | `TranslatorHost.MainWindow` | **1 ✅** |
| Clipboard | `TranslateSelected` 内剪贴板备份/恢复/ClipWait | `Translator.Core.Clipboard` | 3（**copy 写入 ✅**；选中捕获备份/恢复随阶段 5 热键迁移） |
| Hotkey | `ChangeHotkey` `CaptureRound` `ApplyCapture` `MouseSideHook`、`FormatHotkey` `KeysJson` | `TranslatorHost.Hotkey` | 5 |
| Tray | `UpdateMenuLabels` `AddTrayItem` `BuildLangMenu` `SetSourceLang` `SetTargetLang` | `TranslatorHost.Tray` | 5 |

### UI → React + TypeScript（阶段 4，按页渐进，双轨并存）

| 页面 | 现有实现 | React 落点 | 迁移顺序 |
|---|---|---|---|
| Settings | `scripts/html/settings.html`（legacy 保留回退） | `webui/src/settings` | **4a ✅（2026-09-01，双轨：webui/dist 优先，TFD_LEGACY_UI=1 回退）** |
| Result | `scripts/html/result.html` | `webui/src/result` | **4b ✅（2026-09-01，双轨同 4a）** |
| Capture | `scripts/html/capture.html` | `webui/src/capture` | **4c ✅（2026-09-01，双轨同 4a）** |
| Config | `scripts/html/config.html` | `webui/src/config` | **4d ✅（2026-09-01，双轨同 4a）** |
| Input（输入翻译） | 无（新增能力：热键唤起多行输入 → translate 带文本） | `webui/src/input`（复用 result 页组件与 css） | **优化 6 ✅（2026-09-05，五页）** |

**webui 构建约束**：vite-plugin-singlefile 单入口限制 → 按页构建（`TFD_PAGE=<name> npx vite build`，页面在 `vite.config.ts PAGES` 登记）；产物 `webui/dist/<page>.html`（自包含，约 200KB，低于 NavigateToString 2MB 上限）；`webui/src/bridge/protocol.ts` 为页面侧协议唯一实现（post/on/installBridge，双通道接收：chrome.webview message 主 + `__recv` 兜底）。

### Infrastructure → 随各阶段落 C#

| 模块 | 现有实现 | C# 落点 | 阶段 |
|---|---|---|---|
| HTTP | WinHttp COM（`WinHttp.WinHttpRequest.5.1` 同步调用） | `HttpClient` + CancellationToken | **3 ✅** |
| JSON | 手拼字符串 `Q`/`JsonEsc`、正则 `DecodeUnicode`、响应正则解析 | ~~System.Text.Json~~ **JavaScriptSerializer**（GAC `System.Web.Extensions`，阶段 3 实施时调整：net48 离线零新增部署 DLL；业务只经 `JsonUtil` 单点，未来替换成本低） | **3 ✅** |
| Logging | `LogDbg` / `tfd_debug.log` | 迁移期双写：AHK `tfd_debug.log` + C# `%TEMP%\tfd_host_err.log`（[host]/[core]，不记密钥与正文） | **3 ✅** |
| Transport | ~~Named Pipe~~（迁移期测试面已随阶段 7 删除） | 无独立 Transport：页面消息走 WebView2 postMessage/`__recv` 双通道，业务全部进程内直调 | **7 ✅** |

## 边界规则（迁移期硬约束；阶段 6 起为稳态架构规则）

1. **WinForms 职责边界**：Host 只承载 Window lifecycle / WebView2 / DWM 效果 / 置顶 / 托盘。Form 类内禁止出现翻译、Provider、配置、协议业务——业务进 `Translator.Core` 类库，Form 只消费。未来换 WinUI 3 不影响 Core/UI。
2. **进程内直调**：宿主窗口的业务调用一律进程内直调（PageBusiness/Core）；页面消息走 WebView2 双通道，无任何跨进程桥。
3. **Configuration 唯一 Owner = C# ConfigStore**（阶段 3 移交、阶段 6 起 AHK 不存在）：宿主启动接管 `scripts\config.conf`（`TFD_CONFIG` 可覆盖）；`hotkey.conf` 兼容读取保留（config 缺失时仅热键回退）。任一时刻仅一个进程写文件，禁止双向写。
4. **UI 只消费统一模型**：`TranslationResult {sourceText, translatedText, sourceLanguage, targetLanguage, provider, elapsedMs}`，永不接触 Provider 原始 JSON。
5. **Translation Core 第一天就是 async**：`Task<TranslationResult> TranslateAsync(TranslationRequest, CancellationToken)`；分片循环每片检查取消；取消源=窗口关闭/重试/宿主退出。
6. **页面消息全宿主承接**：`PageBusiness.HandlePageEvent` 是页面消息唯一入口（translate/copy/close/drag/热键捕获/设置业务/ui_event），未消费即丢弃记日志——无任何透传兜底。
