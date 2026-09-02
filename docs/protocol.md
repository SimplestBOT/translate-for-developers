# Protocol 契约（v1.4）

> 本文档是 UI ↔ Host 之间的**长期稳定接口契约**。React 前端与 C# 宿主直接实现/消费本协议；信封与消息均为 Transport 无关（页面消息走 WebView2 postMessage/`__recv` 双通道）。
> **v1.1（阶段 3）**：新增 `config_set`、`hello.configPath/configOwner`、`init.testError`（调试字段）；宿主开始承接 `translate`/`copy` 页面消息。
> **v1.2（阶段 5a）**：新增桥上 `hotkey_capture`/`hotkey_apply`/`hotkey_cancel`（A→C）与 `hotkey_changed`/`hotkey_changed_ack`（C→A）；capture/settings 页热键捕获消息流改由宿主承接。
> **v1.3（阶段 6）**：**AHK 退役，宿主纯独立模式**；`hotkey_*` 桥消息与 `page_event` 透传删除。
> **v1.4（阶段 7）**：**Named Pipe 桥测试面整体删除**（`hello`/`open_window`/`push`/`config_set`/`close_window`/`window_ready`/`window_closed`/`page_event` 全部移除，`docs` 中原 §6 作废）；宿主仅存页面级信封；`init.testError` 调试字段随 push 路径删除；`e2e-synth` 合成客户端退役。页面↔宿主协议（§2/§3/§4）自 v1.1 起未变，major 保持 1、minor 3→4。

## 1. 消息信封

所有消息（页面↔宿主）使用统一信封：

```json
{
  "v": 1,
  "type": "translate",
  "requestId": 7,
  "payload": { }
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `v` | int | 协议 major 版本，收端校验（见 §5） |
| `type` | string | 消息类型 |
| `requestId` | int/string | 请求方生成；响应/结果消息必须携带同一 requestId 配对。事件型消息（无请求来源）可为 0 |
| `payload` | any | 页面→宿主为参数数组 `[...]`；宿主→页面为对象 `{...}` 或 `null` |

**兼容规则（收端必须遵守）**：
- 未知 `type` → 静默忽略
- 未知字段 → 忽略
- major 版本不匹配 → 回 `error` 帧（code=`version_mismatch`）并拒绝处理
- minor 版本只增不改，不校验

## 2. 页面 → 宿主（页面侧 `post(type, ...args)`）

| type | payload 示例 | 语义 | 页面 |
|---|---|---|---|
| `ready` | `[]` | 页面加载完成，请求 init | 全部 |
| `close` | `[]` | 请求关闭窗口 | 全部 |
| `drag` | `[]` | 标题栏拖动（仅宿主未启用原生拖动时发出） | 全部 |
| `copy` | `["text"]` | 复制文本到剪贴板 | result |
| `translate` | `[]` | 发起翻译（requestId 用于配对 result/error） | result |
| `apply` | `[]` | 应用捕获的热键 | capture / settings |
| `recapture` | `[]` | 重新捕获热键 | capture / settings |
| `cancel` | `[]` | 取消并关闭（旧捕获页） | capture |
| `cancelCapture` | `[]` | 取消捕获（设置页内联，不关窗） | settings |
| `captureHotkey` | `[]` | 开始捕获热键 | settings |
| `applyHotkey` | `[]` | 应用捕获的热键（设置页，不关窗） | settings |
| `setLang` | `["src"\|"tgt", "id"]` | 设置源/目标语言 | settings |
| `setProvider` | `["mymemory"\|"baidu"]` | 切换提供商 | settings |
| `saveBaidu` | `["appid", "secret"]` | 保存百度密钥并启用 | config / settings |
| `openurl` | `["https://..."]` | 打开外链（仅允许 fanyi-api.baidu.com） | config |
| `ui_event` | `["事件名"]` | **v1.1 React 页**：页面生命周期/诊断上报（如 `settings-rendered`）；收端（宿主）记诊断日志，未知事件名静默忽略；自动化断言锚点 | React 页 |

## 3. 宿主 → 页面（宿主侧 `window.__recv(envelope)`）

| type | payload | 语义 | 页面 |
|---|---|---|---|
| `init` | 见下 | 页面初始化数据（响应 ready） | 全部 |
| `result` | TranslationResult | 翻译结果（requestId 配对 translate） | result |
| `error` | `{code, message}` | 统一错误帧（requestId 配对请求） | 全部 |
| `capturing` | `null` | 热键捕获开始 | capture / settings |
| `captured` | `{hk, keys:[]}` | 捕获到热键（hk=AHK 原始串，keys=键帽数组） | capture / settings |
| `captureCancelled` | `null` | 捕获取消（Esc/鼠标中断） | capture / settings |
| `hotkeyUpdated` | `{hk, keys:[]}` | 热键已应用 | capture / settings |
| `providerUpdated` | `{provider}` | 提供商已切换 | settings |
| `baiduSaved` | `{ok:true}` | 密钥已保存 | settings |

**result 页 init payload**：
```json
{"srcText":"...", "srcLangLabel":"AUTO", "tgtLangLabel":"ZH-CN",
 "provider":"MyMemory", "providerKey":"mymemory", "ndrag":true}
```

**settings 页 init payload**：
```json
{"hotkey":"Ctrl+Alt+D", "hotkeyKeys":["Ctrl","Alt","D"],
 "src":"auto", "tgt":"zh-CN", "provider":"mymemory",
 "hasKeys":true, "appid":"...", "secret":"...", "ndrag":true,
 "langs":[{"id":"auto","name":"自动检测","auto":true,"common":true},
          {"id":"zh-CN","name":"简体中文","common":true}, ...]}
```

## 4. 统一数据模型（阶段 3 起由 C# Translation Core 产出）

```json
{
  "sourceText": "原文",
  "translatedText": "译文",
  "sourceLanguage": "auto",
  "targetLanguage": "zh-CN",
  "provider": "mymemory",
  "elapsedMs": 812
}
```

UI 只消费此模型与 `{code, message}` 错误帧，**永不接触 Provider 原始 JSON**。

**当前 error code 表**：`translate_failed` / `hotkey_invalid` / `no_capture` / `hotkey_busy` / `no_baidu_keys` / `save_failed`（`version_mismatch`/`bad_message`/`window_failed` 为 v1.4 前桥时代遗留码，页面仍按统一错误帧渲染）

## 5. 版本兼容

- `v` 只含 major。major 变更 = 破坏性变更（字段删除/语义变化）；新增字段或消息类型仅提升 minor。
- 页面↔宿主不经信封校验握手（WebView2 同进程通道天然可信）；页面按「未知 type 静默忽略、未知字段忽略」消费。

## 6. 宿主行为要点（v1.4，桥时代章节已随 Named Pipe 删除）

**宿主承接 translate**：自开 result 窗携带待译文本（SetPendingText）；页面 `translate` 到达时以暂存文本 + 当前配置（ConfigStore 现读）调用 Translator.Core，`result`/`error` 帧按 requestId 配对推回页面。取消源：窗口关闭 / 重试（新请求取消旧请求）/ 宿主退出。

**宿主→页面推送（双通道）**：`PostWebMessageAsJson`（React 页 webui/src/bridge/protocol.ts 的主通道）+ `ExecuteScriptAsync window.__recv(...)` 注入。`NavigationCompleted` 前到达的推送入队，完成后按序放行（WebView2 就绪竞态防护，阶段 4a 引入）。

**宿主承接热键捕获**：capture/settings 页的捕获消息流（`captureHotkey`/`recapture`/`apply`/`applyHotkey`/`cancel`/`cancelCapture` + `capturing`/`captured`/`captureCancelled`/`hotkeyUpdated` 帧）由宿主 `CaptureManager`（WH_KEYBOARD_LL + WH_MOUSE_LL 钩子状态机）承接。单例会话 + 30s 超时 + 窗口关闭即销毁。捕获期间宿主按 config 拦截当前翻译热键组合与鼠标侧键。应用 = ConfigStore 写盘 + 宿主立即重注册翻译热键（托盘菜单同步刷新）。Esc 语义：capture 页=取消并关窗（closeOnEsc=true），settings 内联=仅取消（推 captureCancelled）。

**result 页 init payload**：
```json
{"srcText":"...", "srcLangLabel":"AUTO", "tgtLangLabel":"ZH-CN",
 "provider":"MyMemory", "providerKey":"mymemory", "ndrag":true,
 "hotkey":"Ctrl+Alt+D", "hotkeyKeys":["Ctrl","Alt","D"], "langs":[...],
 "src":"auto", "tgt":"zh-CN", "hasKeys":true}
```
设置字段（v1.2/阶段 5d 引入）供主窗口设置 Popover 复用 settings 组件与同一批消息；变更生效后页面补发 `translate`（ConfigStore 每次请求现读，语言/提供商即时生效）。

**生命周期（v1.4）**：宿主**常驻纯独立运行**——启动即托盘+全局翻译热键+选中捕获（配置自 `TFD_CONFIG` 或 `<bridge>\..\..\scripts\config.conf`）。进程仅经托盘「退出」或 `--open` 调试窗关闭/超时结束。调试参数：`--selftest`（无头自检）、`--open <page>[,text]`（独立模式直接开页，60s 安全阀）、`TFD_HEADLESS=1`（无托盘无热键）、`TFD_CONFIG`（配置路径覆盖）、`TFD_TEST_REUSE=1`（自动驱动结果窗复用流程）。`TFD_PIPE_NAME` 仅作实例互斥 Mutex 键（历史名）。`ndrag` = 宿主是否启用原生非客户区拖动（决定页面是否发送 `drag` 消息）。

## 7. 测试锚点

- `translator-ui --selftest`：宿主自检（WebView2 运行时探测 + Core 配置读写冒烟）
- `translator-core.tests`（`dotnet run --project csharp\TranslatorCore.Tests`）：Core 自测（分片/Md5/配置往返/Provider 解析/统一模型，无依赖断言 runner；阶段 3 起。原计划 xUnit，因离线环境改内置 runner，用例粒度不变）
- `TFD_TEST_REUSE=1`：结果窗复用全链路自动化（开窗→9s 后换文本重推 init 同窗重翻）
- `--open <page>[,text]`：四页渲染锚点人工/截图验证（CDP 远程调试可用 `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>` 附加）
- 本文档各 payload 示例即 Protocol 一致性测试的用例来源
- 环境注记：宿主诊断日志在 `%TEMP%\tfd_host_err.log`（含 [core] HTTP 请求级日志，不记录密钥与正文）
