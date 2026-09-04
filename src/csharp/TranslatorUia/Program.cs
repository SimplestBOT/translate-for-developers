//=============================================================
// translator-uia - UIA 选区直读隔离子进程（P1，2026-09-03）
// 父进程（translator-ui）划词捕获时启动本进程读取前台焦点控件的
// TextPattern 选区。设计动机：UIA 跨进程 COM 在目标程序（尤其 Electron/
// Chromium 的 accessibility 冷激活或挂死）可能无限阻塞——2026-09-03 实测
// 在宿主进程内直连时，ZCode 前台一次捕获即把宿主后续 OLE 剪贴板调用
// 一并拖挂（划词整体失死）。独立进程 + 父进程 Kill(2s) + 自杀定时器(4s)
// 双保险，宿主进程零 UIA COM 暴露。
// stdout 协议（单行，base64 规避控制台编码坑）：
//   TFD-UIA:<base64-utf8 选区文本>   命中
//   TFD-UIA-MISS:<原因>              不支持/无选区/异常
//=============================================================
using System;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

internal static class Program
{
    // 与 TranslatorCore/Infrastructure/UiaText.cs 的 MaxChars 镜像保持一致
    //（helper 刻意零项目依赖，宿主侧由 Core 单测覆盖同一判定逻辑）
    private const int MaxChars = 8192;

    // AV 等损坏性异常 .NET4 默认不可捕获——标记必须放在【含 catch 的方法】上
    //（标在被调方法上无效，2026-09-03 实测）；本进程是一次性 disposable 进程，
    // 即便捕获仍漏网，子进程崩溃父进程也无感回退，宿主零影响
    [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
    [System.Security.SecurityCritical]
    private static int Main()
    {
        // 自杀定时器：父进程异常死亡时不留孤儿（正常路径父进程 2s 即 Kill）
        using (new Timer(delegate { Environment.Exit(3); }, null, 4000, Timeout.Infinite))
        {
            try
            {
                ReadAndPrint();
            }
            catch (Exception ex)
            {
                Miss("error:" + ex.GetType().Name);
            }
            return 0;
        }
    }

    private static void ReadAndPrint()
    {
        AutomationElement el = AutomationElement.FocusedElement;
        if (el == null) { Miss("no-focus"); return; }
        object pat;
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat))
        {
            Miss("no-textpattern");
            return;
        }
        TextPatternRange[] sel = ((TextPattern)pat).GetSelection();
        if (sel == null || sel.Length == 0) { Miss("empty-selection"); return; }
        // 只取第一段选区。maxLength 用 -1：实测传有限长度（8192）会踩
        // UiaCoreApi.RawTextRange_GetText 的 AccessViolationException（2026-09-03
        // 沙箱实测）——全量取回后托管侧截断，尺寸风险由 4s 自杀定时器 +
        // 父进程 Kill(2s) 兜底
        string raw = sel[0].GetText(-1);
        if (string.IsNullOrWhiteSpace(raw)) { Miss("empty-selection"); return; }
        if (raw.Length > MaxChars) raw = raw.Substring(0, MaxChars);
        Console.WriteLine("TFD-UIA:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
    }

    private static void Miss(string why) { Console.WriteLine("TFD-UIA-MISS:" + why); }
}
