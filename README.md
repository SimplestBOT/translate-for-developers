# Translate for Developers

选中英文文本，按热键即弹出中文翻译。读代码注释、看英文文档、翻论文摘要——**任意软件里都能用**。

在 MATLAB 里选中一段英文注释看不懂？VS Code 遇到英文报错？PDF 里卡壳？
选中 → 按热键 → 译文立刻弹出。

## 为什么做这个工具

有道词典等划词翻译软件依赖系统高亮读取选中文本，但在 **MATLAB（Java 控件）等程序里无法取词**。
本工具改用**剪贴板方案**：选中后自动 `Ctrl+C` → 读取剪贴板 → 翻译 → 弹窗展示，
对 MATLAB、VS Code、浏览器、PDF 阅读器……**所有支持复制的软件**通用。

## 功能

- 任意软件选中英文 → 按热键翻译（默认 `Ctrl+Alt+D`，可随时改）
- 剪贴板自动备份/恢复，**不破坏**你复制的内容
- 翻译服务可选：**MyMemory**（免费、无需注册）/ **百度翻译**（免费版、质量更好）
- 长文本自动分片，突破百度单次 6000 字节限制
- 托盘菜单：**改热键（直接在键盘上按）**、**切换翻译服务**、重新加载、退出
- 编译为单个 exe，绿色免安装，不放 C 盘也能跑

## 使用

1. 下载 **Release** 里的 `translator.exe`，双击运行（无需安装）
2. 在任意软件中**选中英文** → 按 **`Ctrl+Alt+D`** → 弹出译文窗口
3. 托盘图标（T 字）→ 右键菜单：改热键 / 切换服务 / 退出

## 配置

配置文件 `config.conf`（放在 exe 同目录，自动生成）：

```ini
hotkey=^!d              ; 翻译热键（托盘菜单可改）
provider=mymemory       ; mymemory 或 baidu
baidu_appid=            ; 百度翻译 APP ID（选百度时填）
baidu_secret=           ; 百度翻译密钥（选百度时填）
```

> 托盘菜单改热键/切换服务后会自动写入此文件，无需手动编辑。

## 翻译服务说明

| 服务 | 费用 | 注册 | 说明 |
|---|---|---|---|
| MyMemory | 免费 | 无需 | 每日约 5 万字符额度，响应约 1 秒 |
| 百度翻译 | 免费 | 需要 | 质量更稳，长文本分片支持 |

**开通百度免费版**：fanyi-api.baidu.com → 登录 → 管理控制台 → 创建应用（通用文本翻译/标准版）
→ 复制 APP ID 和密钥 → 托盘菜单「切换服务」→ 百度 → 填入密钥。

## 从源码构建

需要 [AutoHotkey v2](https://www.autohotkey.com) 和 [Ahk2Exe](https://github.com/AutoHotkey/Ahk2Exe)：

```
Ahk2Exe.exe /in src\translator.ahk /out translator.exe /icon src\icon.ico /base AutoHotkey64.exe
```

## 目录结构

```
translate-for-developers/
├── README.md
├── LICENSE
├── .gitignore          # 已排除 config.conf / hotkey.conf（含密钥，勿提交）
└── src/
    ├── translator.ahk  # 主脚本（AutoHotkey v2）
    └── icon.ico        # 程序图标（白底黑 T）
```

## 常见问题

- **按热键没反应**：确认托盘有 T 图标；确认选中了含英文的文本
- **提示"未检测到选中的文本"**：先选中文本再按热键（部分程序需先点一下让焦点在窗口内）
- **提示"网络请求失败"**：检查网络；MyMemory 偶发超时，重试即可
- **想换翻译服务**：托盘菜单 → 切换翻译服务
- **快捷键和别的软件冲突**：托盘菜单 → 更改翻译热键 → 直接按你想要的组合键
- **开机自启（可选）**：把 translator.exe 的快捷方式放进 `shell:startup`

## License

[MIT](LICENSE)
