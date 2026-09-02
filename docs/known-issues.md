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
