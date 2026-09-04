//=============================================================
// SelectionProviders.cs - 选中文本 Provider 链（P1：UIA 隔离子进程直读 + 剪贴板回退）
// 链路（SelectionCapture.CaptureCore 编排）：
//   1. UiaSelectionProvider：启动 translator-uia.exe 读前台焦点控件
//      TextPattern 选区——不注入按键、不触碰剪贴板（成功即翻译，剪贴板
//      零污染；终端前台同样优先 UIA，是 TerminalGuard 之外第二层防误杀）。
//   2. 回退：现有剪贴板捕获流程（CaptureCore 主体原样；终端目标仍绝不
//      注入 Ctrl+C）。
// 隔离设计（2026-09-03 首版教训）：首版在宿主进程内直接调用
//   System.Windows.Automation——ZCode(Electron) 前台时 UIA 跨进程 COM 阻塞，
//   且把同进程后续 OLE 剪贴板调用一并拖挂（划词整体失死，日志止于
//   "uia timeout" 后无任何输出）。现改为独立子进程：父进程
//   WaitForExit(TimeoutMs) 超时即 Kill，子进程另有 4s 自杀定时器防孤儿；
//   宿主进程零 UIA COM 暴露，目标程序卡死最多 2s 后无感回退剪贴板路径。
// 负缓存（2026-09-03 增量）：已知 UIA 不兼容/挂死的目标窗口一次明确失败
//   （超时/崩溃/不支持）后 Cooldown 时间内同目标跳过 UIA 直接走剪贴板
//   回退，避免每次热键都重付 2s 超时+子进程启动；成功即清除；到期自动
//   重试（非永久黑名单）。仅进程内存、无持久化。键=前台窗口 hwnd。
// Win32 WM_GETTEXT Provider：仍不实现（EM_GETSEL+WM_GETTEXT 拿控件全文本
//   非选区，风险>收益），链路留扩展位。
//=============================================================
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TranslatorHost
{
    internal static class UiaSelectionProvider
    {
        /// <summary>应急开关：TFD_UIA=0 关闭 UIA 直读（回退纯剪贴板路径）。
        /// 默认开启——查询在独立子进程执行，宿主进程不受目标程序卡死影响。</summary>
        private static readonly bool Enabled = Environment.GetEnvironmentVariable("TFD_UIA") != "0";

        /// <summary>UIA 查询上限。正常目标（含子进程冷启动）<500ms；超时=
        /// Kill 子进程按 miss 处理（无感回退剪贴板路径）。</summary>
        private const int TimeoutMs = 2000;

        /// <summary>负缓存 TTL：明确失败后此时间内同目标跳过 UIA，到期自动
        /// 允许重试（"几秒后自动再试"，非永久黑名单）。</summary>
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

        /// <summary>负缓存：目标 hwnd → 冷却到期时刻。仅内存；成功即清、
        /// 到期自除。hwnd 在 TTL 内被系统复用的概率可忽略（即便误命中也
        /// 10s 自愈）。</summary>
        private static readonly ConcurrentDictionary<long, DateTime> CooldownUntil
            = new ConcurrentDictionary<long, DateTime>();

        /// <summary>读取前台焦点控件的真实选中文本。true=拿到可直接使用；
        /// false=不支持/无选区/超时/近期失败被跳过（why=诊断原因，不含文本
        /// 内容）。target=前台窗口 hwnd（缓存键，由调用方 CaptureCore 传入）。</summary>
        public static bool TryReadSelection(long target, out string text, out string why)
        {
            text = null;
            if (!Enabled)
            {
                why = "disabled";
                Program.LogHost("capture uia: off (TFD_UIA=0)");
                return false;
            }
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translator-uia.exe");
            if (!File.Exists(exe))
            {
                why = "helper-missing";
                Program.LogHost("capture uia: miss (" + why + ") -> clipboard path");
                return false;
            }

            // 负缓存：近期明确失败 → 跳过（省 2s 超时+子进程启动）；到期 → 移除重试
            DateTime until;
            if (CooldownUntil.TryGetValue(target, out until))
            {
                double left = (until - DateTime.UtcNow).TotalSeconds;
                if (left > 0)
                {
                    why = "cooldown";
                    Program.LogHost("capture uia: skip target=0x" + target.ToString("X")
                        + " (recent fail, " + Math.Ceiling(left) + "s left) -> clipboard path");
                    return false;
                }
                CooldownUntil.TryRemove(target, out _);
                Program.LogHost("capture uia: cooldown expired, retry target=0x" + target.ToString("X"));
            }
            else
            {
                Program.LogHost("capture uia: attempt target=0x" + target.ToString("X"));
            }

            string line;
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                using (var p = Process.Start(psi))
                {
                    var so = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(TimeoutMs))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        why = "timeout(" + TimeoutMs + "ms)";
                        CacheIfDeterministic(target, why);
                        Program.LogHost("capture uia: " + why + " -> clipboard path");
                        return false;
                    }
                    so.Wait(500);   // 进程已退出，管道关闭，短暂等待读取任务收尾
                    line = so.Status == TaskStatus.RanToCompletion ? so.Result : null;
                }
            }
            catch (Exception ex)
            {
                why = "spawn-error:" + ex.GetType().Name;
                Program.LogHost("capture uia: miss (" + why + ") -> clipboard path");
                return false;
            }

            line = (line ?? "").Trim();
            if (line.StartsWith("TFD-UIA-MISS:", StringComparison.Ordinal))
                why = line.Substring(13);
            else if (line.StartsWith("TFD-UIA:", StringComparison.Ordinal))
            {
                try
                {
                    text = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(8)));
                    why = "textpattern";
                }
                catch (Exception) { why = "decode"; text = null; }
            }
            else why = "unexpected-output";

            if (text == null)
            {
                CacheIfDeterministic(target, why);
                Program.LogHost("capture uia: miss (" + why + ") -> clipboard path");
                return false;
            }
            CooldownUntil.TryRemove(target, out _);   // 成功即清除该目标的失败状态
            Program.LogHost("capture uia: hit len=" + text.Length + " (" + why + ")");
            return true;
        }

        /// <summary>确定性/高成本失败才进负缓存（重试大概率同样失败且要重付
        /// 代价）；瞬态原因不缓存——下次触发可能就成功。timeout 分支在
        /// Kill 处直接调用（why 带 "(2000ms)" 后缀，故按前缀匹配）。</summary>
        private static void CacheIfDeterministic(long target, string why)
        {
            bool cacheable =
                (why != null && why.StartsWith("timeout(", StringComparison.Ordinal))   // 目标挂死
                || why == "unexpected-output" || why == "decode"                        // helper 崩溃/坏输出
                || why == "no-textpattern"                                              // 目标不支持（确定性）
                || why == "error:AccessViolationException"
                || why == "error:SEHException";                                         // 原生崩溃（Chromium AV 家族）
            // 不缓存：no-focus / empty-selection / 其它 error:*（焦点切换等瞬态）
            if (cacheable)
            {
                CooldownUntil[target] = DateTime.UtcNow + Cooldown;
                Program.LogHost("capture uia: cooldown set target=0x" + target.ToString("X")
                    + " (" + (int)Cooldown.TotalSeconds + "s)");
            }
        }
    }
}
