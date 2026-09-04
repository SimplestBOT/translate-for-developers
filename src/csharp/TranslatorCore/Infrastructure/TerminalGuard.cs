//=============================================================
// TerminalGuard.cs - 前台目标终端分类（划词捕获防 Ctrl+C 误杀）
// 终端宿主（conhost/Windows Terminal/ConEmu…）与带集成终端的编辑器
// （VS Code/JetBrains…）里，注入的 Ctrl+C 会向前台进程发送中断信号
// （SIGINT），杀掉正在运行的任务（npm run dev、编译等）；无选中文本时
// 误按热键同样触发。捕获前按窗口类名 + 进程名分类：命中 → 捕获路径
// 改用 Ctrl+Insert（无副作用的复制键），绝不注入 Ctrl+C。
// 纯函数（无 P/Invoke）：宿主取 ClassName/ProcessName 后传入，可单测。
//=============================================================
using System;
using System.Collections.Generic;

namespace Translator.Core.Infrastructure
{
    public static class TerminalGuard
    {
        // 终端宿主的窗口类名（GetClassName，精确匹配、忽略大小写）
        private static readonly HashSet<string> Classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ConsoleWindowClass",             // conhost：cmd / PowerShell / Far 等
            "CASCADIA_HOSTING_WINDOW_CLASS",  // Windows Terminal
            "VirtualConsoleClass",            // ConEmu / cmder
            "PuTTY",
            "mintty"                          // Git Bash / MSYS2 / Cygwin
        };

        // 终端宿主进程名（exe 去扩展名，精确匹配、忽略大小写）
        private static readonly HashSet<string> Processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd", "powershell", "pwsh", "conhost", "wt", "windowsterminal",
            "conemu", "conemu64", "mintty", "putty", "alacritty", "wezterm",
            "wezterm-gui", "hyper", "warp", "tabby", "terminus",
            "xshell", "securecrt", "ttermpro", "mobaxterm", "windterm"
        };

        // 进程名前缀（覆盖带版本/架构后缀的变体，如 Code - Insiders / idea64）。
        // 带集成终端的编辑器/IDE：无法从窗口区分焦点在编辑器还是终端面板，
        // 编辑器内 Ctrl+Insert 仍是复制键（无副作用），故一律按终端处理。
        private static readonly string[] ProcessPrefixes =
        {
            "code",       // VS Code / VSCodium / Code - Insiders
            "devenv",     // Visual Studio
            "studio",     // studio64（Visual Studio / Android Studio）
            "idea", "pycharm", "webstorm", "rider", "clion", "goland",
            "phpstorm", "rubymine", "datagrip", "fleet"   // JetBrains
        };

        /// <summary>前台目标是否终端类宿主。命中时 reason 给出判定依据
        /// （class:xxx / proc:xxx）供诊断日志。</summary>
        public static bool IsTerminal(string windowClass, string processName, out string reason)
        {
            reason = null;
            if (!string.IsNullOrEmpty(windowClass) && Classes.Contains(windowClass))
            {
                reason = "class:" + windowClass;
                return true;
            }
            if (string.IsNullOrEmpty(processName)) return false;
            if (Processes.Contains(processName))
            {
                reason = "proc:" + processName;
                return true;
            }
            foreach (string p in ProcessPrefixes)
            {
                if (processName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "proc:" + processName;
                    return true;
                }
            }
            return false;
        }
    }
}
