# 遗留问题登记（阶段 4a 完成时）

> 原则：不为修 AHK 而回头重构。AHK 是迁移期 Legacy，随阶段 5/6 退役。
> 逐项给出归属阶段与处置方向。

## 1. 窗口尺寸/DPI 显示偏小 —— **已解决（阶段 5a，2026-09-01）**
- 处置：宿主 `app.manifest` 声明 PerMonitorV2 DPI 感知（WebView2 内容随屏 DPI 自缩放）+
  `MainWindow` 按 LOGPIXELSX 把 AHK 逻辑尺寸（96 基准）放大到物理像素。
- 范围注记：仅修复 C# 宿主窗口（正式运行路径）。`-preview` 本地 WvWindow（AHK）降级
  路径仍为 legacy 冻结行为，随阶段 6 退役，不再单独修复。
- 需用户桌面复验（多屏异 DPI 场景）。

## 2. 更改热键一次后，第二次无法正常打开（捕获窗/设置窗）—— **已解决（阶段 5a，2026-09-01）**
- 处置：热键捕获整体迁宿主 `Hotkey/CaptureManager`（WH_KEYBOARD_LL + WH_MOUSE_LL 钩子
  状态机，AHK InputHook/CaptureRound 冻结不复刻）：每轮捕获 = 新会话（钩子/定时器/
  待应用键随会话创建销毁），单例互斥（重复请求回 hotkey_busy）+ 30s 超时 + 窗口关闭/
  管道断开即销毁——无跨轮残留状态。应用 = ConfigStore 写盘 + C→A `hotkey_changed` →
  AHK 重注册翻译热键（新状态机按协议 v1.2 重写，不复刻该缺陷）。
- 沙箱验证：连续三轮捕获会话（含超时自然清会后重开）均正常，无 busy 残留；
  真实按键捕获流程需用户桌面实测。

## 3. 设置集中化：热键/语言/Provider 等集中到翻译主窗口
- 现状：设置目前独立窗口；用户期望在翻译主窗以组件/Popover 管理。
- 归属：**阶段 4 后续页（result/result 主窗 React 化时）+ 阶段 5（热键/托盘迁 C#）**。
- 已做准备：Settings 页已拆成受控组件（`webui/src/settings/components/`——LangSelect / ProviderPicker / BaiduKeysCard / HotkeyCard），props 驱动、无页面耦合，可直接嵌入主窗 Popover；消息编排（App.tsx 的 on/post 编排层）按窗口维度拆分即可复用。
- 本阶段不实施集中化（按用户指示）。

## 4. MyMemory AHK 翻译异常（阶段 0–2 遗留）
- 处置（按用户指示）：**不修复旧 AHK 实现**。以 C# TranslatorCore 为准（阶段 3 已验证 MyMemory 真实翻译 200/2-3s，见 tfd_host_err.log [core] 日志）。
- 若 React/C# 集成中出现 MyMemory 仍失败：修 `TranslatorCore/Providers/MyMemoryProvider.cs`，不回退 AHK。
- AHK 内的 TranslateMyMemory/TranslateBaidu 仅服务本地 WvWindow 降级路径（宿主缺失时），冻结不再改动。

## 阶段 4b 新增已知问题
- **宿主双通道推送对 React 页产生重复帧**：MainWindow.Push 对每帧同时走
  PostWebMessageAsJson（主通道）与 ExecuteScriptAsync `__recv` 注入（legacy 兜底），
  React 页两条通道都挂（installBridge），同一信封到达两次；且两通道到达的信封
  **键序不同**（PostWebMessageAsJson 经宿主解析再序列化、对象键被重排为字母序，
  `__recv` 注入保留 AHK 原始 JSON 键序），直接字符串比较判不出重复。settings 页
  处理器幂等所以 4a 未暴露；result 页 init 非幂等（init→post('translate')），
  4b 验证时暴露为 translate 双发（rid=2/rid=4 两次 HTTP 请求）。
- 处置：页面协议层吸收——`webui/src/bridge/protocol.ts` recv() 按「键序无关
  规范化（递归排序对象键）后完全相同的信封，2s 窗口内去重」，保证 on() 处理器
  每帧只执行一次。对 legacy 页无影响（只挂 __recv 单通道）。
- 长期方向：宿主按页面类型单通道推送（React 页仅 PostWebMessageAsJson），
  随阶段 5 Window 模块演进；届时页面侧去重保留为纵深防御。
- 宿主冷启动期 `__recv` 注入可能延迟 500ms+ 才执行（WebView2 冷启动 + React
  挂载竞争主线程），去重窗口取 2s 的依据。
- AHK ResultMsgHandler 新增 case "ui_event" 记日志（result-rendered 自动化锚点），
  与 SettingsMsgHandler 同款，legacy 行为不变。

## 阶段 4a 新增已知问题
- React 19 + WebView2 内嵌产物中 StrictMode 会导致 init 回环失败（prod 下本应 no-op，
  此环境特例，原因未深究）——webui 各页入口**不使用 StrictMode**（settings/main.tsx 有注记）。
- vite-plugin-singlefile 不支持多入口（inlineDynamicImports 限制）→ webui 按
  `TFD_PAGE=<name> npx vite build` 逐页构建（vite.config.ts 的 PAGES 登记）。
- 测试环境（沙箱）限制： computer-use 截图全黑、AHK FileAppend/stdout 不可靠、
  bash→AHK→Run 孙进程被拦——React 页交互细节（点击/下拉/热键捕获实操）需用户桌面实测；
  自动化验证覆盖到「页面加载/ready→init→settings-rendered/消息收发」层（宿主日志可查）。

---

## 阶段 6 退役注记（2026-09-02）

- 问题 1 的 AHK `-preview` 本地 WvWindow 路径、问题 4 的 AHK 翻译实现，已随 AHK 退役整体删除
  （`scripts\translator.ahk`/`ui.ahk`/`bridge.ahk`/`lib\`，备份于 `E:\translator\backup-stage6-ahk\`）。
- 问题 2 的「应用 = 写盘 + C→A hotkey_changed → AHK 重注册」职责切分已消解：
  v1.3 起宿主写盘后**立即自重注册**托盘热键（协议 v1.3，桥 hotkey_* 五消息删除）。
- 新登记（阶段 7 处置）：**热键键帽显示缺陷**——config hotkey=`^!H`（注册日志铁证，
  ParseHotkey/注册成功）但 UI 键帽与 capture 页「当前热键」显示 `Ctrl+Alt+!H`
  （FormatHotkey/KeysJson 的正则剥前缀行为与显示不符，疑似全角/转义类问题，
  真实热键串待与用户核对 config.conf 后确认）。纯显示问题，注册与触发功能正常。

## 阶段 6 新增已知问题
- （无——e2e-synth 三处测试客户端缺陷已在阶段 6 修复：Q 函数缺失、测试页 post payload
  违反协议（字符串未包装为数组，宿主 HandleCopy/GetList 静默丢弃）、等待窗过短与
  剪贴板残留短路；另 synth configPath 改指 %TEMP% 测试文件，不再触碰真实 config.conf）

---

## 阶段 7 收口（2026-09-02）

- **键帽显示缺陷已修复**：根因=FormatHotkey/KeysJson 的正则字符类 `[\x5E\x21\x2B\x23]`
  存在两个叠加错误——`\x21` 后接 `\x2B` 使 `\x` 转义吞位歧义 + 类内 `^` 未转义，
  编译产物实为**否定字符类** `[^!+#]`（剥掉主键、留下前缀 "!"）。改为 `[\^!+#]+` 后
  `^!H` → `Ctrl+Alt+H`（csc 探针 7 组样例 + settings 页 CDP 实测终验）。
- Named Pipe 桥测试面/e2e-synth/test 诊断页/legacy html/全部排障探针已退役
  （备份 `backup-stage7-legacy\`）；诊断回归面收缩为：--selftest、Core 56 断言、
  TFD_TEST_REUSE、--open 四页锚点（可附 CDP）。
- 文档漂移修正：protocol.md 曾记载的 `TFD_WEBUI_DIR` 宿主从未实现，已删。
- 迁移收官：阶段 0-7 全部完成，AHK=0、桥=0。

---

## 收官后首修（2026-09-02，用户报设置页语言下拉三症状）

- **现象**：单独设置窗（非翻译窗 Popover）点开语言下拉：无法上下滚动、无法选择语言、
  下拉面板透明度太高（下层卡片文字透过来）。
- **根因**（与 5d result 页 Popover 同族，settings 页漏网）：四页 `.rv` 入场动画
  `fill-mode:forwards` 使卡片在动画结束后**永久保留层叠上下文**（Blink 对 filling
  动画持续保留）；语言下拉 `.dd{position:absolute;z-index:30}` 被困在「翻译语言」卡的
  上下文内，溢出卡外的部分被 DOM 靠后的「翻译服务/密钥」半透明卡（`--card:rgba(...,.032)`）
  整块盖住 → 命中测试/滚轮全落在盖板卡上（点不到+滚不动），下层文字透上来（看着透明）。
- **修复**：四页 `.rv` 改 `fill-mode:backwards`（只保延迟期防闪），动画结束回到自然态，
  层叠上下文消失，`.dd` 的 z-index:30 回到根上下文生效。视觉无变化（`to` 关键帧本=自然态），
  顺带消除 forwards 在动画失效时元素永久隐形（base opacity:0）的隐患。result 页 Popover
  5d 已 portal 化不受此影响；capture/config 页同隐患一并预防修复。
- **验证**（CDP 真窗取证，隔离实例 TFD_PIPE_NAME+TFD_CONFIG+独立 udd）：修复前
  `elementFromPoint(下拉列表中部)`=`provrow`（服务卡）、真实鼠标点击落在服务卡 `<i>` 上；
  修复后=`dditem`，点「日语」→标签变「日语▼」+setLang 落盘 `tgt_lang=ja`，滚轮
  `ddScroll 0→720 / pageScroll 0`，before/after 截图对比透明消失。result 页回归
  （wrapH=540+rendered 锚点）通过。
- **部署注记**：纯 webui 资产修复，无 C# 改动；宿主每次开窗现读 dist HTML，常驻实例
  无需重启即生效（重开设置窗即可）。

## 收官后二修（2026-09-02，用户报原文中文字体与译文不一致）

- **现象**：结果窗原文里的中文发虚、和译文明显不是一个字体；用户问是否字号原因。
- **根因**（CDP `CSS.getPlatformFontsForNode` 取证实锤）：原文 `.srcbox` 用纯西文等宽栈
  `--mono`（Cascadia Code→…→monospace，无一含汉字字形），中文落到系统 monospace
  兜底=**NSimSun（新宋体）**；译文 `.dstbox` 走 `--sans` 落 **Microsoft YaHei UI**。
  是字体家族不同，不是字号（字号也不同：12.5px vs 14px，但字形差异主因是家族）。
- **修复**：result.css 新增 `--monocjk`（等宽栈尾、generic monospace 前插
  'Microsoft YaHei UI'），`.srcbox` 改用。逐字符回退特性保证英文仍 Cascadia Code
  （等宽不丢），中日文字与译文同款雅黑，韩文照旧 Malgun Gothic。
- **验证**：before=srcbox [NSimSun×22, Cascadia Code×38, Malgun Gothic×5]，
  after=[Cascadia Code×38, Microsoft YaHei UI×22, Malgun Gothic×5]；dstbox 不变
  [Segoe UI Variable×40, Microsoft YaHei UI×22, Malgun Gothic×5]。截图复核。

## 收官后三修（2026-09-03，终端 Ctrl+C 误杀防护）

- **风险**（用户提出，最高优先级）：划词捕获向前台注入 Ctrl+C，前台为终端时
  （cmd/PowerShell/Windows Terminal/VS Code 集成终端等）Ctrl+C = SIGINT，会中断
  正在运行的任务（npm run dev、编译等）；无选中文本误按热键同样触发。
- **修复（宿主 SelectionCapture）**：
  1. 新增 `TranslatorCore/Infrastructure/TerminalGuard.cs`（纯函数，可单测）：
     按窗口类名（ConsoleWindowClass=conhost 全家 / CASCADIA_HOSTING_WINDOW_CLASS=
     Windows Terminal / VirtualConsoleClass=ConEmu / PuTTY / mintty）+ 进程名
     （cmd/powershell/pwsh/conhost/wt/alacritty/hyper…，IDE 前缀 code/devenv/
     studio/idea/pycharm/…）分类。**IDE 一律按终端处理**：窗口层无法区分焦点在
     编辑器还是集成终端面板，而编辑器内 Ctrl+Insert 仍是复制键（无副作用），
     宁宽勿漏。
  2. `CaptureCore` 进入时判定（IsTerminalTarget：GetClassName+进程名 →
     TerminalGuard）：命中终端 → 尝试梯 4 发全为 Ctrl+Insert，**绝不注入
     Ctrl+C**；失败气球明示「为避免 Ctrl+C 误中断运行中的任务，未注入 Ctrl+C」。
     未命中 → 原路径不变（3×Ctrl+C + 末发 Ctrl+Insert）。诊断日志在
     `capture target` 行追加 `terminal=class:xxx/proc:xxx`，attempt 行标注
     每发注入模式（ctrl+c / ctrl+ins）。
  3. **顺带修复实锤 bug**：`InjectCtrlInsert` 的 Insert 键未带
     `KEYEVENTF_EXTENDEDKEY` → 系统当作**小键盘 0**（VK_INSERT 双重绑定），
     旧实现实际注入的是 Ctrl+Numpad0，从未真正生效。SendKey 增加 extended
     参数，Insert down/up 均带标志。
  4. Core 自测新增 `TerminalGuardTests`（11 断言，总数 56→67）：类名/进程名/
     前缀命中、大小写、浏览器与记事本不误判、空输入安全。
- **部署验证**：build 0 警告 0 错误；Core 67 全绿；selftest PASS；宿主重启后
  `hotkey registered: ^!H` 正常。真实终端划词（PowerShell 跑长任务 + 选中输出
  按热键翻译、任务不被中断）待用户桌面实测。
- **长期方向（登记未实施）**：UI Automation TextPattern.GetSelection 静默读取
  选中文本（Windows Terminal/conhost/Chromium 均暴露 UIA），剪贴板注入降级为
  fallback——顺带消除剪贴板污染。缓行原因：UIA 跨进程调用无内建超时，目标
  繁忙时可能挂起（捕获线程 Join 上限 6s 会被吃满），需独立线程+超时包络，
  改动面较大；当前 Ctrl+Insert 路径已消除误杀风险。

## P1：UIA 选区直读（2026-09-03，用户提出并限定范围）

- **需求**：新增 UIA Selection Provider 优先直读目标控件真实选中文本（成功后
  不注入任何按键）；失败/无选区/不支持 → 无感回退现有剪贴板捕获；UIA 跨进程
  调用必须有超时隔离；保留 TerminalGuard（终端绝不注入 Ctrl+C）；模块化
  Provider 链，不大改架构。WM_GETTEXT Provider 本轮不实现（EM_GETSEL+
  WM_GETTEXT 仅覆盖原生 Edit 且拿的是控件全文本，误把全文档当选区风险大于
  收益；链路留扩展位）。
- **实现**：
  1. `TranslatorHost/SelectionProviders.cs`（新增）——`UiaSelectionProvider.
     TryReadSelection`：`AutomationElement.FocusedElement` → `TextPattern.
     GetSelection()` → 取第一段 range `GetText(MaxChars)`。**超时隔离**：查询
     在专用后台线程（IsBackground）执行，外层 `Join(2000ms)` 到点按 miss
     放弃，残留线程不阻塞进程退出；线程体内任何异常按 miss 处理（焦点切换
     瞬间 ElementNotAvailable 是常态）。日志只记 len 与原因（textpattern/
     no-focus/no-textpattern/empty-selection/timeout/error:类型名），
     **不记录选中文本内容**。
  2. `CaptureCore` 编排：诊断日志+终端判定之后、备份剪贴板之前插 UIA 尝试
     ——成功直接返回（不触碰剪贴板，seq 不变）；失败无感落回原剪贴板流程。
     终端前台同样优先 UIA（终端选区若可静默直读则连 Ctrl+Insert 都不用发，
     是 TerminalGuard 之外第二层防误杀；miss 则仍走 Ctrl+Insert 路径）。
  3. `TranslatorCore/Infrastructure/UiaText.cs`（新增，纯函数）：候选有效性
     判定（null/空/纯空白→miss）+ 超过 `MaxChars=8192` 截断（防 provider
     缺陷返回全文档）；不 Trim，与剪贴板路径行为一致。Core 测试 +8 断言
     （总数 67→75）。
  4. csproj 增 framework 引用 UIAutomationClient/UIAutomationTypes（GAC 自带，
     离线零部署依赖）。`CaptureSelectedText` 外层 Join 6000→8000ms（预算=
     UIA 2s + 剪贴板梯 ~5.2s）。
- **部署验证**：build 0 警告 0 错误；Core 75 全绿；selftest PASS；宿主重启
  后热键注册正常。
- **手动测试清单**（桌面实测）：
  ① Edge/Chrome 页面选中→热键：日志应现 `capture uia: hit`，剪贴板 seq 不变
  （零污染）；② 记事本选中→热键：同上（Win32 Edit 有 UIA TextPattern）；
  ③ VS Code 编辑器/集成终端选中→热键：hit 或 miss→Ctrl+Insert，日志确认
  从未出现 ctrl+c；④ 无选区直接按热键：`uia miss (empty-selection)` → 原
  路径失败气球；⑤ 目标程序卡死（可用挂起窗口模拟）：`uia timeout(2000ms)`
  → 回退剪贴板路径，热键不长时间卡住；⑥ 行为回归：浏览器划词翻译结果与
  旧版一致。
- **已知边界**：① Chromium 系（Edge/Chrome/VS Code）accessibility 为惰性
  激活，进程首次 UIA 查询可能 miss/变慢，后续查询正常（miss 即回退剪贴板，
  无感）；② 个别 provider 缺陷应用可能把全文档冒充选区（GetSelection 实现
  不当）——8192 截断兜底，实测遇到再按进程名针对性排除；③ 提权目标窗口
  UIA 同样受 UIPI 限制（miss 回退，与剪贴板路径一致）。
- **首版事故与重设计（2026-09-03 凌晨，用户实测报"用不了"）**：
  - 首版在**宿主进程内**直接调 System.Windows.Automation（专用线程+Join 2s
    超时）——ZCode(Electron) 前台实测：UIA 跨进程 COM 阻塞不止拖住查询线程，
    **还把同进程后续 OLE 剪贴板调用一并拖死**（日志止于 `uia timeout` 后
    零输出，划词整体失死；旧版同前台正常）。"放弃残留线程"挡不住 COM 层面
    的进程内污染，要求 4 的"超时/隔离"必须物理隔离。
  - **重设计：UIA 查询挪入独立子进程 `translator-uia.exe`**（新工程
    `csharp/TranslatorUia/`，零项目依赖）：stdout 单行协议
    `TFD-UIA:<base64-utf8>`（命中）/`TFD-UIA-MISS:<原因>`；父进程
    `WaitForExit(2000ms)` 超时即 Kill，子进程另有 4s 自杀定时器防孤儿；
    **宿主进程零 UIA COM 暴露**。应急开关 `TFD_UIA=0` 关闭直读。selftest
    增加 uia-provider 冒烟（不计 PASS/FAIL）。
  - **实战验证**：子进程首跑即撞 `TextPatternRange.GetText` 的
    **AccessViolationException**（UiaCoreApi.RawTextRange_GetText 原生层，
    传有限 maxLength 与 -1 均复现；System.Windows.Automation 托管包装层
    对部分 provider 的已知缺陷）——**进程隔离价值当场兑现**：子进程崩溃，
    宿主无感（stdout 无输出 → `miss(unexpected-output)` → 回退）。
  - **AV 捕获注记**：corrupted-state 异常 .NET4 默认不可捕获，
    `[HandleProcessCorruptedStateExceptions]` 必须标在**含 catch 的方法**
    上（标在被调方法上无效，实测踩坑）；现已捕获转结构化
    `miss(error:AccessViolationException)`。
  - **预期兼容矩阵（待桌面实测确认）**：conhost/Windows Terminal/Win32 Edit
    类目标 UIA 可命中（零注入零剪贴板触碰）；**Chromium 系（ZCode/浏览器/
    VS Code）GetText 大概率 AV → 必 miss → 回退剪贴板路径**（行为等同
    终端防护版）。若要覆盖 Chromium，后续把 helper 内部换成 COM 直调
    （IUIAutomation），stdout 协议不变——独立进程架构使该替换零风险。
  - 部署：build-bridge.ps1 增 translator-uia 构建+部署；**便携包发布需补
    translator-uia.exe**。宿主重启验证：热键 ^!H 注册正常，selftest PASS。
- **负缓存增量（2026-09-03，用户要求：减少已知失败场景的重复尝试）**：
  目标 hwnd 键的进程内 `ConcurrentDictionary`（无持久化，TTL 10s）——
  **确定性/高成本失败**（timeout / helper 崩溃 unexpected-output / decode /
  no-textpattern / error:AV|SEH）后同目标跳过 UIA 直接回退剪贴板（日志
  `uia: skip (recent fail, Ns left)`），不再重付 2s 超时+子进程启动；
  **瞬态原因不缓存**（no-focus / empty-selection / 其它 error:*——下次
  触发可能就成功）；**成功即清除**；到期自动重试（`cooldown expired,
  retry`），非永久黑名单。缓存键=前台 hwnd（CaptureCore 传入）。selftest
  增第二探针：首查缓存型 miss 后次查应 `miss(cooldown)`（沙箱实测：
  attempt→no-textpattern→cooldown set 10s→skip 10s left）。helper 子进程/
  2s 超时/自杀保护/剪贴板回退/终端拦截/TFD_UIA=0 全部未动。

## 优化 2：Provider 降级 + 重试 + 缓存（2026-09-03）

- **需求**（用户提出）：MyMemory 限 500 字符且偶发超时、百度需注册——主
  Provider 失败自动切备用；指数退避重试；同文本 5min 缓存省额度；错误码
  可视化（429/超时/鉴权分别提示，不出裸「网络请求失败」）。
- **实现（全部在 TranslatorCore，宿主零改动）**：
  1. **错误码**：`TranslateException` 增 `Code` 字段（timeout/network/
     rate_limited/auth/server/http/parse/input/unknown）+ 工厂方法
     `Http/Timeout/Network`（两 Provider 共用文案映射：429→限流、5xx→服务端
     错误、连接失败→网络）。解析层分类：MyMemory responseStatus 403/429/
     配额文案→rate_limited；百度 52003/54001→auth、54003→rate_limited。
     **协议 error 帧 code 恒为 translate_failed（v1.4 不动）**——分类码用于
     Service 决策+诊断日志，页面文案已按类区分。
  2. **ResultCache**（新文件 Translation/ResultCache.cs）：键=语言对+\u0001+
     原文（与 Provider 无关，降级结果同样命中）；TTL 5min + 容量 64 LRU；
     时钟注入可单测。
  3. **TranslationService 编排**：缓存优先（命中 elapsedMs=0 直出）→ 主
     Provider 最多 3 次尝试、指数退避 500/1000ms（**仅网络类瞬时错误：
     network/rate_limited/server/http**；timeout 不重试——15s 已付出；auth/
     parse 确定性失败不重试）→ 失败降级备用（MyMemory↔百度互备，百度作
     主/备都需密钥）→ 全败组合报错（主错误在前+「备用提供商也失败」附注，
     保留主错误码）。成功结果 Provider 字段=实际成功方（页面徽标可见降级
     事实）。日志 [core] translate: fail/success/backoff/cache hit（只记
     code/attempt/provider/len 元数据，不记文本）。
- **测试**：Core 75→**110 断言**（+35：ResultCache TTL/LRU/键隔离；错误码
  分类矩阵；假 Provider 离线验证 重试成功/超时降级/鉴权降级/双败组合文案/
  无备用直抛/取消不被吞/缓存命中省请求/语言对隔离）。真实链路探针：
  MyMemory 1766ms → 同文本二刷 elapsed=0 缓存命中。
- **部署**：build 0 错、selftest PASS、宿主重启热键注册正常。**测试脚本坑
  留档**：readonly 数组只能改元素不能重赋值（CS0198）；Git Bash 下 csc.exe
  需 cwd 内相对路径（反斜杠折叠+盘符路径解析双坑，MSYS2_ARG_CONV_EXCL 救不了
  绝对路径，正斜杠盘符路径也被吃）。
- **回归修复（2026-09-03 当日，用户实测"切服务商不自动重翻"）**：缓存键
  原不含 Provider——切换服务商后页面自动补发 translate（机制完好，已核实
  result/App.tsx retranslate），但同语言对+同文本命中旧服务商缓存结果
  （译文/徽标原样返回），肉眼即"没重翻"。修复=**缓存键加入配置 Provider
  （MakeKey 4 参）**：切服务商即新键真实重翻；切回原 Provider 仍命中原
  缓存（省额度语义保留）；降级结果以配置键存储（TTL 内重复划词同样命中）。
  测试 +7 断言（117 总）：切百度真实重翻/新 Provider 译文/切回命中原缓存。

## 优化 3：配置密钥 DPAPI 加密（2026-09-03）

- **需求**（用户提出）：config.conf 明文存百度 APP ID/密钥——DPAPI 加密落盘；
  确认日志/崩溃 dump 不打印 key。
- **实现**：
  1. `Configuration/SecretProtector.cs`（新）：`ProtectedData`（CurrentUser
     范围，无附加熵——密钥安全完全由 DPAPI 用户凭据承载）→ `dpapi:<base64>`
     行内密文；Unprotect 失败（换机/换账户/损坏）返回空串降级（自动回
     MyMemory 路径），用户重存密钥即恢复；明文值原样透传（旧版兼容读）。
  2. `ConfigStore`：写侧恒加密（baidu_appid/baidu_secret 两行）；读侧前缀
     检测解密、明文透传；内存快照恒明文（业务可用）。
  3. **启动迁移**：PageBusiness.InitFromConfigPath 检测文件内密钥行无
     `dpapi:` 前缀 → WriteSync 立即加密回写（幂等，日志「明文行已消除」；
     回写失败不阻塞启动，下次保存动作重试）。真实 config.conf 已迁移。
  4. csproj 增 framework 引用 System.Security（GAC 自带，零部署依赖）。
- **验证**：
  - Core 117→**142 断言**（+25：加解密回环/空值与 null 行为/损坏密文降级/
    落盘无明文+回读一致/旧版明文文件兼容读+迁移回写/密文行前缀）。
    旧断言「磁盘含原始密钥（特殊字符不转义）」随新行为改为「磁盘无明文」。
  - **真实配置零暴露扫描**（verify-secrets.ps1 工作区留存）：迁移后本机
    DPAPI 解密取值（内存中）→ 扫 config.conf/宿主日志/仓库全树 →
    `dpapi-lines=2` + 六项全 **CLEAN**。
  - 日志审计依据：Http 只记 URL host（不含查询串）；translate 链路只记
    code/len 元数据；异常文案（含 Network 工厂）不携带 URL/密钥；宿主无
    崩溃 dump 生成点。selftest 增「密钥落盘必须 dpapi: 前缀」断言。
  - `.gitignore` 已含 config.conf/hotkey.conf（仓库既有，核实未动）。
- **边界**：① 换机/换 Windows 账户后密文不可解 → 密钥按未配置处理，重存
  即恢复（README 已注明）；② 手工把配置改回明文可用且可读，但下次保存
  会重新加密（明文读路径长期保留为逃生口）；③ 便携包首次运行生成默认
  空密钥配置（空值不加密不包前缀）。

## 优化 4：DeepL + OpenAI-compatible LLM Provider（2026-09-03，v1.6）

- **需求**（用户提出并限定范围）：接入 DeepL 官方 API；通用 OpenAI-compatible
  Provider（不做每家一个类）；开发者文本 placeholder 保护；复用现有
  fallback/retry/cache；最小 UI；错误码人话化。
- **Core 新增**：
  - `Providers/DeepLProvider.cs`：POST v2/translate（默认免费端点
    api-free.deepl.com，`deepl_endpoint` 可切 Pro），DeepL-Auth-Key 头；
    语言码 en→EN/zh-CN→ZH（zh-TW 原样传由服务端裁决，不硬编码支持表）；
    HTTP 456=配额尽→rate_limited，401/403→auth。
  - `Providers/OpenAICompatibleProvider.cs`（唯一 LLM 实现）：POST
    `{base}/chat/completions`，system=Prompt+语言指令、temperature=0.1；
    空 Key（Ollama）不带 Authorization 头；401/403→auth、400→bad_request
    （随 http 不重试族）、429→rate_limited。
  - `Providers/ProviderCatalog.cs`：显示名目录 + 5 预设模板
    （OpenAI/DeepSeek/Kimi/智谱/Ollama）+ custom + DefaultLlmPrompt
    （只返回译文/保格式/不改代码/占位符规则）。**预设=纯配置模板**，
    落盘字段为最终事实（llm_preset 仅 UI 回显）。
  - `Providers/DevTextGuard.cs`：placeholder 保护（仅 LLM 路径）——``` 围栏/
    行内码/URL/Win+Unix 路径/带扩展名文件名/CONSTANT_CASE/snake_case/
    lowerCamelCase 点号链 → `__TFD_G<序号>__`；**恢复校验两道**：已知
    占位符缺失/改写 → 失败；恢复后残留未知 `__TFD_G*__`（LLM 幻觉）→
    失败——均抛 TranslateException 走重试/降级，不静默；零命中原文直译
    （普通英文不受影响）；原文已含 __TFD_G 字样跳过保护防冲突。
  - Http 增 PostFormAsync/PostJsonAsync（统一异常映射）。
- **配置**：AppConfig +7 字段（deepl_key/deepl_endpoint/llm_preset/
  llm_base_url/llm_api_key/llm_model/llm_prompt），deepl_key+llm_api_key
  走 DPAPI（verify-secrets.ps1 已扩 4 密钥行扫描）；provider 白名单扩
  deepl/llm；config.conf 13 键。
- **Service**：factory 增 deepl/llm 分支（未配置返回 null）；primary 解析
  门禁降级（llm 未配置→mymemory）；**缓存键含实际主 Provider**（v1.6 起
  用 ResolvePrimaryId 而非原始配置值，门禁降级后键一致）；fallback 链
  保持 主→MyMemory（免费无门槛兜底）。
- **Host**：setProvider 门禁（未配置→provider_not_ready 错误帧，页面引导
  配置卡）；saveDeepl/saveLlm 消息（deeplSaved/llmSaved 帧）；托盘
  Provider 行=「已配置项循环」（mymemory 恒在环内）；settings init 增
  deepl*/llm* 字段；result 页 provider 显示名走 ProviderCatalog。
- **UI（最小集）**：ProviderPicker 4 卡；新 DeepLCard（key+endpoint）/
  LlmCard（预设下拉+baseUrl+key+model+prompt textarea，预设=表单填充）；
  密钥卡按所选 Provider 条件渲染（未配置 Provider 不显示卡）；settings
  版本 chip→v1.6；settings.css 补 select/textarea。
- **验证**：Core 142→**187 断言**（+45：DeepL 解析/语言码/预设目录/LLM
  请求体与解析/DevTextGuard 命中-恢复-两道失败检测-普通英文零命中/门禁
  降级/工厂产出/13 键 DPAPI 往返）。build 0 错；selftest PASS；隔离实例
  settings-rendered 锚点过；真实探针：13 键写盘+MyMemory 2008ms+缓存 0ms
  （原功能零回归）。DeepL/LLM 真实请求需用户自备 Key（未硬编码任何密钥）。
- **待用户桌面实测**：① 设置页选 DeepL/AI 大模型→填 Key→翻译；② 预设
  切换填充；③ Ollama 本地（Key 留空）；④ LLM 划词含代码/路径文本确认
  占位符保护生效；⑤ 托盘 Provider 循环切换。

## 优化 4 实测首修（2026-09-03 晚，用户桌面实测暴露三连）

1. **DeepL 403 根因（实锤）**：鉴权头格式实现错误——把 `DeepL-Auth-Key`
   当成**独立头名**发送，官方要求 `Authorization: DeepL-Auth-Key <key>`
   （方案在 Authorization 头内）。服务器视为未鉴权 → 403 Missing
   Authorization。**用户 Key 自始有效**。三段式诊断法（全部零暴露）：
   ① 存量日志分析（URL 规范化后仍 403 → 排除客户端 URL 问题）；
   ② 一次性探针内存解密 Key 直发官方 API——裸 .NET Framework 进程先撞
   「未能创建 SSL/TLS 安全通道」（ServicePointManager 未显式开 Tls12 的
   环境差异；宿主 Http.cs 已有补丁不受影响），显式开 Tls12 后官方返回
   `403 Missing Authorization header` 实锤；③ 改用正确格式 → **HTTP 200
   真实翻译成功**（hello→你好）。修复后 Core 196 断言全绿，部署重启。
2. **端点规范化**：用户填裸主机名 `api-free.deepl.com`（缺 scheme/路径）
   → HttpClient 无效请求（network 错）。NormalizeEndpoint：空=默认；
   无 scheme 补 https://；裸主机补 /v2/translate；完整 URL 原样（+6 断言）。
3. **MyMemory 当日配额耗尽**（21:53 起连续 rate_limited）：免费号每日约
   5 万字符——DeepL 失败降级也失败，放大"全坏了"观感。次日自动恢复。
4. **设置 Popover 显示不全（用户报）**：v1.6 ProviderPicker 2 卡→4 卡后
   面板内容超出 540px 结果窗，被 body overflow:hidden 裁底。修复=
   `.spov` max-height:calc(100vh-110px)+overflow-y:auto 面板内滚动；
   error 分支补 provider_not_ready → hint 提示。重建 result 页。
