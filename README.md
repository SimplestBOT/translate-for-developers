# Translate for Developers

选中英文文本，按热键即弹出中文翻译。读代码注释、看英文文档、翻论文摘要——**任意软件里都能用**。

在 MATLAB 里选中一段英文注释看不懂？VS Code 遇到英文报错？PDF 里卡壳？
选中 → 按热键 → 译文立刻弹出。

## 为什么做这个工具

有道词典等划词翻译软件依赖系统高亮读取选中文本，但在 **MATLAB（Java 控件）等程序里无法取词**。
本工具改用**剪贴板方案**：选中后自动 `Ctrl+C` → 读取剪贴板 → 翻译 → 弹窗展示，
对 MATLAB、VS Code、浏览器、PDF 阅读器……**所有支持复制的软件**通用。

## 功能

- 任意软件选中文本 → 按热键翻译（默认 `Ctrl+Alt+T`，可随时改）
- **全新深色 UI（v1.1）**：基于系统自带 WebView2 渲染，玻璃质感卡片、入场级联动效、
  扫描光束加载条、骨架屏、键帽弹簧动效；翻译改为**异步**，窗口秒开、动画不卡顿
- **源语言自动检测**（也可手动指定），**目标语言 30+ 种**（中英日韩法德西俄…）
- 剪贴板自动备份/恢复，**不破坏**你复制的内容
- 翻译服务可选：**MyMemory**（免费、无需注册）/ **百度翻译**（免费版、质量更好）
- 长文本自动分片，突破百度单次 6000 字节限制
- 交互细节：标题栏可拖动、`Esc` 关闭、`Enter` 复制译文、复制按钮变绿打勾、
  右下角 Toast 通知（替代生硬的 MsgBox）
- 托盘菜单：**改热键（直接在键盘上按）**、**切换翻译服务**、选语言、退出
- 绿色免安装：C# 宿主 + WebView2 渲染（系统自带 Runtime），不放 C 盘也能跑

## 使用

1. 运行 `start-translator.bat`（或直接运行 `src/bridge/translator-ui.exe`），托盘出现 T 字图标
2. 在任意软件中**选中英文** → 按 **`Ctrl+Alt+T`** → 弹出译文窗口
3. 译文窗口：拖顶部标题栏可移动；`Esc` 关闭；`Enter` 或「复制译文」复制
4. 托盘图标（T 字）→ 右键菜单：改热键 / 切换服务 / 选语言 / 退出

## 配置

配置文件 `config.conf`（与 `bridge/` 同级的 `scripts/` 目录，首次运行自动生成）：

```ini
hotkey=^!d              ; 翻译热键（托盘菜单可改）
src_lang=auto           ; 源语言：auto=自动检测，或 zh-CN/en/ja/ko/…
tgt_lang=zh-CN          ; 目标语言（默认简体中文）
provider=mymemory       ; mymemory 或 baidu
baidu_appid=            ; 百度翻译 APP ID（选百度时填）
baidu_secret=           ; 百度翻译密钥（选百度时填）
```

> 托盘菜单改热键/切换服务后会自动写入此文件，无需手动编辑。

## 支持的语言

源语言默认**自动检测**（也可手动指定）；目标语言可选以下 30+ 种：

| 语言 | 代码 | 语言 | 代码 |
|---|---|---|---|
| 简体中文 | zh-CN | 英语 | en |
| 繁体中文 | zh-TW | 日语 | ja |
| 韩语 | ko | 法语 | fr |
| 德语 | de | 西班牙语 | es |
| 葡萄牙语 | pt | 俄语 | ru |
| 意大利语 | it | 阿拉伯语 | ar |
| 印地语 | hi | 泰语 | th |
| 越南语 | vi | 印尼语 | id |
| 土耳其语 | tr | 荷兰语 | nl |
| 波兰语 | pl | 乌克兰语 | uk |
| 希腊语 | el | 捷克语 | cs |
| 瑞典语 | sv | 匈牙利语 | hu |
| 罗马尼亚语 | ro | 丹麦语 | da |
| 芬兰语 | fi | 挪威语 | no |
| 马来语 | ms | 菲律宾语 | fil |
| 孟加拉语 | bn | 乌尔都语 | ur |
| 波斯语 | fa | 希伯来语 | he |

> 托盘菜单 → 「源语言」「目标语言」子菜单即可切换，选择自动保存。

## 系统要求

- Windows 10 1903+（含最新补丁）或 Windows 11（.NET Framework 4.8 运行时为系统自带）
- WebView2 Runtime（**Win10/11 系统自带**，绝大多数机器无需任何安装）

## 翻译服务说明

| 服务 | 费用 | 注册 | 说明 |
|---|---|---|---|
| MyMemory | 免费 | 无需 | 每日约 5 万字符额度，响应约 1 秒 |
| 百度翻译 | 免费 | 需要 | 质量更稳，长文本分片支持 |

**开通百度免费版**：fanyi-api.baidu.com → 登录 → 管理控制台 → 创建应用（通用文本翻译/标准版）
→ 复制 APP ID 和密钥 → 托盘菜单「切换服务」→ 百度 → 填入密钥。

## 从源码构建

需要 [.NET SDK](https://dotnet.microsoft.com)（任意近期版本，目标 net48）：

```
dotnet build src/csharp/TranslatorHost/TranslatorHost.csproj -c Release
```

产物在 `src/csharp/TranslatorHost/bin/Release/net48/win-x64/`，把该目录内容拷到
`bridge/`（与 `start-translator.bat` 中路径一致）即可运行；前端页面
（`src/webui/`，Vite + React 19 + TS）按页构建后产物 `webui/dist/<page>.html` 由宿主自动加载。

### 开发调试参数

```
translator-ui.exe --selftest            ; 无头自检（WebView2/配置读写）
translator-ui.exe --open result,文本     ; 调试开窗（60s 安全阀自动退出）
TFD_HEADLESS=1                          ; 无托盘无热键（沙箱 e2e）
TFD_PIPE_NAME=<名>                      ; 实例互斥隔离键（并行测试实例）
TFD_TEST_REUSE=1                        ; 自动驱动结果窗复用流程
```

## 架构（v1.5，迁移收官）

- **C# 宿主**（`src/csharp/TranslatorHost`，net48 + WinForms + WebView2）：唯一宿主——
  托盘、全局热键、划词捕获（Ctrl+C 注入）、窗口生命周期、WebView2、DWM 圆角、原生拖动。
  WinForms 只做 Windows 集成，业务不进 Form。
- **业务核心**（`src/csharp/TranslatorCore`，类库）：翻译/Provider/配置/剪贴板/HTTP/JSON，
  全 async + CancellationToken。
- **UI 页面**（`src/webui/`，React 19 + TypeScript）：settings/result/capture/config 四页，
  按页单文件构建；与宿主通过 JSON 消息协议通信（契约见 `docs/protocol.md`）。
- AHK 版本与迁移期 Named Pipe 桥已分别于 v1.4/v1.5 退役删除。

## 目录结构

```
translate-for-developers/
├── README.md
├── LICENSE
├── .gitignore
├── start-translator.bat        # 启动入口（拉起 C# 宿主）
├── docs/                       # architecture / protocol / known-issues
└── src/
    ├── csharp/                 # C# 宿主 + 业务核心 + 自测
    │   ├── TranslatorHost/     # WinForms/WebView2/托盘/热键/划词捕获
    │   ├── TranslatorCore/     # 翻译/Provider/配置/剪贴板（类库）
    │   ├── TranslatorCore.Tests/  # 内置断言自测（dotnet run）
    │   └── build-bridge.ps1    # 构建 + 部署到 bridge/
    ├── webui/                  # React 19 + TS 前端（按页单文件构建）
    │   ├── src/                # settings/result/capture/config + bridge 协议层
    │   └── dist/               # 构建产物 <page>.html（自包含）
    ├── icon.ico
    └── WebView2Loader.dll
```

## 常见问题

- **按热键没反应**：确认托盘有 T 图标；确认选中了含英文的文本
- **提示"未检测到选中的文本"**：先选中文本再按热键（部分程序需先点一下让焦点在窗口内）
- **提示"网络请求失败"**：检查网络；MyMemory 偶发超时，可在窗口里点「重试」
- **想换翻译服务**：托盘菜单 → 切换翻译服务
- **快捷键和别的软件冲突**：托盘菜单 → 更改翻译热键 → 直接按你想要的组合键
- **译文窗口怎么移动**：按住顶部标题栏（logo 一行）拖动即可
- **首次启动弹"找不到 WebView2"**：到 [微软官网](https://developer.microsoft.com/microsoft-edge/webview2/) 装一次 Evergreen Runtime（正常 Win10/11 不需要）
- **开机自启（可选）**：把 translator.exe 的快捷方式放进 `shell:startup`

## License

[MIT](LICENSE)
