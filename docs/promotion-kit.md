# 推广渠道自荐文案包（promotion kit）

> 由 issue《【建议】推广渠道梳理与自荐渠道汇总》催生。所有文案可直接复制使用；
> 事实核对基于 v1.7.0（2026-09-06）。投稿前如版本更新，注意替换版本号与热键默认值。
>
> 事实基准：默认热键 划词 Ctrl+Alt+T / 截图 Ctrl+Alt+Z / 输入 Ctrl+Alt+I（均可改）；
> MIT；Win10 1903+；便携 zip 约 870KB；Release v1.7.0。

## 0. 标准简介（三档长度）

**一句话版**：

> 选中英文按热键即弹中文翻译——MATLAB、终端等传统划词工具失效的场景也能用，开源免费。

**100 字版（issue 原版微调）**：

> Translate for Developers：选中英文按热键即弹出中文翻译，
> 剪贴板 + UIA 双通道取词，MATLAB/终端等传统工具失效的场景也能用。
> 支持 DeepL / 百度 / 任意 OpenAI 兼容大模型（自动保护代码、路径、URL 不被翻译），
> 内置截图 OCR 与输入翻译，绿色免安装，MIT 开源。

**长版（README 开头 / AlternativeTo 用）**：

> 面向开发者的 Windows 划词翻译工具。剪贴板 + UIA TextPattern 双通道取词，
> 终端类前台自动改用 Ctrl+Insert 防止误中断任务；翻译服务可选 MyMemory（免注册）、
> 百度、DeepL、任意 OpenAI 兼容大模型（DeepSeek/Kimi/智谱/Ollama 预设），LLM 翻译
> 自动保护代码/路径/URL/标识符；另附截图 OCR 翻译（Windows 内置引擎，免费离线）
> 与多行输入翻译。C# 宿主 + WebView2 渲染，绿色免安装，密钥 DPAPI 加密，MIT 开源。

## 1. HelloGitHub（issue 自荐）

> **项目名称**：Translate for Developers（划词翻译）
>
> **项目地址**：https://github.com/SimplestBOT/translate-for-developers
>
> **项目简介**：面向开发者的 Windows 划词翻译工具。选中英文按热键即弹出中文译文，
> 对 MATLAB（Java 控件）等传统取词方案失效的场景，采用剪贴板 + UIA TextPattern
> 双通道取词；终端类前台自动改用 Ctrl+Insert，绝不注入 Ctrl+C 误杀运行中任务。
> 翻译服务支持 MyMemory（免注册）、百度、DeepL 与任意 OpenAI 兼容大模型，
> LLM 翻译自动保护代码、路径、URL、标识符不被改写；附带截图 OCR 翻译
> （Windows 内置引擎，免费离线，暗色截图自动反色增强）与多行输入翻译。
> C# 宿主 + WebView2 渲染，绿色免安装（便携 zip 约 870KB），密钥 DPAPI 加密落盘。
>
> **项目截图/演示**：https://github.com/SimplestBOT/translate-for-developers#readme
> （README 顶部有 5 秒演示 GIF）
>
> **适用人群**：开发者、科研人员（MATLAB/论文场景）、经常读英文报错与文档的用户
>
> **开源协议**：MIT

## 2. GitHubDaily（PR 到资源列表）

列表行格式（投到对应分类小节，如「AI 工具」或「实用工具」）：

```markdown
- [Translate for Developers](https://github.com/SimplestBOT/translate-for-developers)：面向开发者的 Windows 划词翻译工具，剪贴板 + UIA 双通道取词（MATLAB/终端场景可用），支持 DeepL/百度/OpenAI 兼容大模型（自动保护代码不被翻译），内置截图 OCR 与输入翻译，绿色免安装。
```

配套 PR 描述：

> 收录自荐：面向开发者的划词翻译工具，v1.7.0 已发布（含截图 OCR 与输入翻译），
> MIT 协议，README 含演示 GIF 与四语文档。特色：MATLAB/终端等取词困难场景可用、
> LLM 翻译的代码内容保护、Windows 内置 OCR 免费离线。

## 3. 阮一峰·科技爱好者周刊（issue 投稿）

> 推荐一个开源工具：Translate for Developers（https://github.com/SimplestBOT/translate-for-developers）。
> Windows 划词翻译，专门解决两个老问题：MATLAB 这类 Java 控件窗口传统取词失效
> （改用剪贴板 + UIA 双通道），以及终端里划词会误发 Ctrl+C 杀掉正在跑的任务
> （终端前台自动只发 Ctrl+Insert）。面向开发者还有两个加分项：LLM 翻译自动保护
> 代码/路径/URL 不被改写；内置 Windows 系统引擎的截图 OCR 翻译，免费离线。
> 绿色免安装，MIT。

## 4. 掘金文章大纲《从 MATLAB 取词说起：我写了一个给开发者的划词翻译工具》

1. **痛点开场**（可直接用的开场段）：
   > 有道词典在 MATLAB 里取不到词——MATLAB 的编辑器是 Java 控件，不走标准
   > 文本框那套 UIA 文本接口。写代码时查个英文注释，要复制→切窗口→粘贴→翻译，
   > 四步。作为一个天天和英文报错打交道的人，我受够了，于是有了这个项目。
2. **方案演进**（技术叙事主线，每节一个真实踩坑）：
   - 剪贴板方案：备份/恢复全格式剪贴板 + 注入 Ctrl+C；扫描码缺失导致 Electron
     应用吞键的坑（keybd_event 必须带 bScan）
   - 终端防护：划词热键在终端 = Ctrl+C = SIGINT，误杀 npm run dev；
     TerminalGuard 分类 + 全程只发 Ctrl+Insert
   - UIA 直读升级：TextPattern.GetSelection 静默取词，独立子进程隔离
    （跨进程 COM 挂死会拖死整个宿主——真实的"线程超时不是隔离"教训）、
     AccessViolationException 的进程隔离价值
3. **面向开发者的翻译层**：LLM 占位符保护（代码/路径/URL/标识符 →
   `__TFD_G*__`，恢复双道校验不静默）；降级链与 5 分钟缓存省额度
4. **加分能力**：截图 OCR（Windows 内置引擎免费离线；暗底自动反色——
   深色 IDE 截图识别率暴跌的修复）；输入翻译
5. **架构一页**：C# 宿主 + WebView2 + React 四/五页，业务全在 Core 类库
6. **工程化**：DPAPI 密钥加密、GitHub Actions CI/CD（tag 自动出便携包）
7. **结尾**：MIT、求 star、求 issue（附 demo.gif 链接）

## 5. V2EX「分享创造」帖（成品）

> **标题**：写了个 Windows 划词翻译工具，专治 MATLAB 取词和终端 Ctrl+C 误杀
>
> 背景：有道/主流划词工具在 MATLAB（Java 控件）里取不到词，而终端里划词翻译
> 还会注入 Ctrl+C 把你正在跑的 npm run dev 杀掉。这两个场景我自己天天撞，
> 所以写了个工具：
>
> https://github.com/SimplestBOT/translate-for-developers
>
> - 剪贴板 + UIA 双通道取词，剪贴板全格式备份恢复，UIA 走独立子进程防挂死
> - 终端类前台（cmd/WT/VS Code 集成终端）自动只发 Ctrl+Insert，绝不 Ctrl+C
> - 翻译后端可选 MyMemory（免注册）/百度/DeepL/任意 OpenAI 兼容大模型；
>   LLM 翻译会把代码、路径、URL 挖空占位再还原，不会把你的函数名翻成中文
> - 附带截图 OCR 翻译（Windows 内置引擎，免费离线）和多行输入翻译
> - C# + WebView2 + React，便携 zip 约 870KB，密钥 DPAPI 加密，MIT
>
> v1.7.0 刚发布，求轻拍，尤其求 MATLAB 用户的实测反馈。

## 6. 知乎回答「Windows 下有什么好用的划词翻译工具」（成品）

> 利益相关：下面这个工具是我自己写的，自己也在天天用。
>
> 主流工具（有道、Pot、TTime、CopyTranslator）覆盖了大多数场景，但有两个场景
> 它们普遍失效：一是 MATLAB——它的编辑器是 Java 控件，基于 UIA 文本接口的
> 取词方案拿不到选区；二是终端——划词工具注入的 Ctrl+C 在终端里是 SIGINT，
> 会直接杀掉你正在跑的编译/`npm run dev`。
>
> 我写的 [Translate for Developers](https://github.com/SimplestBOT/translate-for-developers)
> 就是冲这两个场景来的：剪贴板 + UIA 双通道取词（剪贴板全格式备份恢复，
> UIA 走独立进程带超时隔离），终端类前台自动只发 Ctrl+Insert。
> 另外针对开发者做了 LLM 翻译的代码保护（代码块/路径/URL/标识符先挖空占位、
> 翻译后还原），后端可选 DeepL/百度/MyMemory/任意 OpenAI 兼容模型（含 Ollama 本地）。
> 新版还带 Windows 内置引擎的截图 OCR 翻译（免费离线）和多行输入翻译。
> 绿色免安装，MIT，Win10 自带 WebView2 即可跑。

## 7. Show HN（英文，备好待用户决定）

**标题**：
> Show HN: Translate-for-Developers – hotkey translation that works in MATLAB and terminals

**首评**：
> Hi! I wrote this because existing Windows selection-translators fail in two
> places I live in daily: MATLAB (its editor is a Java control — UIA text
> patterns return nothing) and terminals (the injected Ctrl+C they use is
> SIGINT — it kills your running build).
>
> TFD uses a clipboard channel (full-format backup/restore) plus a UIA
> TextPattern reader running in an isolated helper process with a kill timeout,
> and a foreground classifier that switches to Ctrl+Insert in terminal-class
> windows — it never sends Ctrl+C there. For LLM backends it masks code, paths,
> URLs and identifiers into placeholders before translating and restores them
> after, so your function names don't come back in Chinese. v1.7 added
> screenshot OCR (the in-box Windows engine, free and offline, with auto-invert
> for dark screenshots) and a typed-input mode.
>
> C# host + WebView2 + React, ~870KB portable zip, MIT.
> https://github.com/SimplestBOT/translate-for-developers

## 8. AlternativeTo 条目要素

- Name: Translate for Developers；Platform: Windows
- License: Free and Open Source（MIT）
- Category: Translation Tool
- Website: https://github.com/SimplestBOT/translate-for-developers
- 描述（用上方长版）；Tagline（用一句话版）
- 作为替代品勾选：有道词典、DeepL、Pot、CopyTranslator、QTranslate

## 9. 投放 checklist（对应 issue 三批）

- [ ] 第一批：HelloGitHub / GitHubDaily / 阮一峰周刊（文案见 §1-§3，待主人过目后投稿）
- [ ] 第二批：掘金（§4 大纲，第一人称建议主人亲笔润色）/ V2EX（§5 成品）/ 知乎（§6 成品）——需主人平台账号
- [ ] 第三批：winget-pkgs PR（manifest 模板已就绪 packaging/winget/，填 hash 即可提）/ AlternativeTo（§8，需账号）/ awesome-windows 等 PR（可代办，待确认目标列表）
