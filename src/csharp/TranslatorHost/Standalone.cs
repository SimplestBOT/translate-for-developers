//=============================================================
// Standalone.cs - 宿主独立运行（阶段 5b；阶段 6 起为唯一模式）
// TrayController：托盘图标/菜单（语言、提供商、入口、退出）+ 全局翻译热键
//   （RegisterHotKey + WM_HOTKEY，AHK 串解析）+ 气泡提示。
// SelectionCapture：选中捕获（剪贴板全格式备份 → 注入复制键 → 轮询等待 →
//   读文本 → 恢复剪贴板）；终端类前台（TerminalGuard 判定）只发 Ctrl+Insert，
//   绝不注入 Ctrl+C（终端里 Ctrl+C=SIGINT，误杀运行中的任务）。
// 线程模型：全部在 UI 线程（托盘/热键消息/菜单事件天然同线程）。
//=============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Translator.Core.Configuration;
using Translator.Core.Infrastructure;
using Translator.Core.Providers;

namespace TranslatorHost
{
    // ------------------------------------------------------------
    // 选中捕获
    // ------------------------------------------------------------
    public static class SelectionCapture
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();
        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public System.Drawing.Rectangle rcCaret;
        }
        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO gti);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

        /// <summary>前台窗口的线程内真实键盘焦点窗口诊断（焦点≠前台）。</summary>
        private static string DescribeFocus()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint pid;
                uint tid = GetWindowThreadProcessId(fg, out pid);
                var gti = new GUITHREADINFO();
                gti.cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO));
                if (!GetGUIThreadInfo(tid, ref gti))
                    return "<GetGUIThreadInfo 失败 " + Marshal.GetLastWin32Error() + ">";
                IntPtr focus = gti.hwndFocus;
                if (focus == IntPtr.Zero) return "<无焦点窗口>";
                uint fpid;
                GetWindowThreadProcessId(focus, out fpid);
                string proc = "<>";
                try { proc = System.Diagnostics.Process.GetProcessById((int)fpid).ProcessName; }
                catch (Exception) { }
                var cn = new System.Text.StringBuilder(128);
                GetClassName(focus, cn, 128);
                return proc + "/" + cn + " hwnd=0x" + focus.ToString("X");
            }
            catch (Exception ex) { return "<焦点诊断失败 " + ex.Message + ">"; }
        }
        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("kernel32.dll")]
        private static extern uint GetLastError();

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }
        // 注意：union 必须含 MOUSEINPUT（x64 下 INPUT 总长 40 字节）——只放
        // KEYBDINPUT 会得到 32 字节结构体，cbSize 错误 → SendInput 全部被拒
        //（实测 err=183，2026-09-02）
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        private const uint INPUT_KEYBOARD = 1;

        /// <summary>SendInput 注入（返回实际入队事件数；0=被系统拒绝）。
        /// extended：扩展键标志（Insert 等必须带——缺省会被当作小键盘按键）。</summary>
        private static uint SendKey(ushort vk, bool up, bool extended)
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[0].u.ki.wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            inputs[0].u.ki.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0);
            uint sent = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent == 0)
                Program.LogHost("SendInput 被拒 vk=0x" + vk.ToString("X") + " err=" + Marshal.GetLastWin32Error());
            return sent;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }
        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG msg);
        private const uint PM_REMOVE = 1;

        /// <summary>泵消息等待：裸 STA 线程没有消息泵时，OLE 剪贴板的跨套间
        /// 代理（OleGetClipboard 的 IDataObject 封送）依赖本线程分发窗口消息——
        /// 纯 Thread.Sleep 会让 ContainsText/GetText 静默失败（2026-09-02 实测：
        /// 同序列在 PowerShell 主 STA 线程成功、在 app 裸 STA 线程全灭）。</summary>
        private static void PumpWait(int ms)
        {
            long ticks = DateTime.UtcNow.Ticks + ms * 10000L;
            for (; ; )
            {
                while (PeekMessage(out MSG m, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
                if (DateTime.UtcNow.Ticks >= ticks) return;
                Thread.Sleep(5);
            }
        }

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint MAPVK_VK_TO_VSC = 0;

        /// <summary>单飞锁：捕获进行中忽略新触发。热键长按会自动重复触发 WM_HOTKEY，
        /// 并发捕获会互相 Clear/Restore 剪贴板——轻则全部失败，重则把恢复出的旧
        /// 剪贴板内容冒充"选中文本"（2026-09-02 实测踩中：连按两次，第二次读到
        /// 第一次恢复的旧文本开窗翻译）。</summary>
        private static int _captureBusy;

        /// <summary>复制选中文本（对外入口）。在专用 STA 线程执行——Clipboard 的
        /// Set/Get 需 STA，且不能占用 UI 线程（Sleep 阻塞消息泵会让 Delayed
        /// Rendering 数据拿不到）。返回 null = 未获取到，error 给可显示原因。</summary>
        public static string CaptureSelectedText(out string error)
        {
            error = null;
            if (Interlocked.CompareExchange(ref _captureBusy, 1, 0) != 0)
            {
                Program.LogHost("capture busy-reject（上次捕获未完成，忽略本次触发）");
                error = "上一次捕获尚未完成，请稍候再按";
                return null;
            }
            try
            {
                string result = null;
                string err = null;
                ThreadStart work = delegate { result = CaptureCore(out err); };
                var t = new Thread(work);
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                // 剪贴板是共享资源：被其他进程长期锁定时 Clipboard 调用可能无限
                // 阻塞——Join 上限 8s，超时放弃（残留线程随后自行结束，无副作用）。
                // 预算含 UIA 直读（2000ms 超时）+ 剪贴板尝试梯（~5.2s）
                if (!t.Join(8000))
                {
                    Program.LogHost("capture 超时放弃（STA 线程 6s 未返回，剪贴板被占用?）");
                    error = "捕获超时：剪贴板被其他程序占用";
                    return null;
                }
                error = err;
                return result;
            }
            finally
            {
                Interlocked.Exchange(ref _captureBusy, 0);
            }
        }

        // 物理修饰键：Shift/Ctrl/Alt/LWin/RWin
        private static readonly int[] ModifierVks = { 0x10, 0x11, 0x12, 0x5B, 0x5C };

        /// <summary>热键触发瞬间用户还按着 Ctrl+Alt（WM_HOTKEY 在松开前就到），
        /// 此时注入的 Ctrl+C 会变成 Ctrl+Alt+C，目标应用收不到复制指令——
        /// AHK Send 会先弹起物理键，裸 keybd_event 不会。所以：先等物理键
        /// 释放（≤600ms），超时则强制发一轮修饰键 keyup 兜底。</summary>
        private static void WaitForModifierRelease()
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(600);
            bool held;
            do
            {
                held = false;
                foreach (int vk in ModifierVks)
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0) { held = true; break; }
                if (held) PumpWait(25);
            } while (held && DateTime.UtcNow < deadline);
            if (held)
            {
                // 用户一直按着（少见）：强制弹起全部修饰键，别让注入的 C 被污染
                foreach (int vk in ModifierVks)
                    keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                PumpWait(20);
            }
        }

        /// <summary>注入必须带扫描码：keybd_event bScan=0 的按键会被部分框架
        /// 丢弃（Electron/Chromium 实测忽略——2026-09-02 ZCode 前台捕获全灭的
        /// 根因；AHK Send 自带扫描码所以旧版能用）。</summary>
        private static void KeyTap(byte vk, bool extended)
        {
            byte sc = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            uint down = extended ? KEYEVENTF_EXTENDEDKEY : 0;
            keybd_event(vk, sc, down, UIntPtr.Zero);
            PumpWait(30);
            keybd_event(vk, sc, down | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void InjectCopy()
        {
            Program.LogHost("inject state: ctrl=" + ((GetAsyncKeyState(0x11) & 0x8000) != 0)
                + " alt=" + ((GetAsyncKeyState(0x12) & 0x8000) != 0)
                + " shift=" + ((GetAsyncKeyState(0x10) & 0x8000) != 0));
            SendKey(0x11, false, false);
            PumpWait(30);
            SendKey(0x43, false, false);
            PumpWait(30);
            SendKey(0x43, true, false);
            SendKey(0x11, true, false);
        }

        /// <summary>Ctrl+Insert：终端安全复制键（不发 SIGINT）。Insert 必须
        /// 带 EXTENDEDKEY——缺省会被当作小键盘 0，旧实现因此从未真正生效
        /// （2026-09-03 修复；终端防护路径也依赖此函数）。</summary>
        private static void InjectCtrlInsert()
        {
            SendKey(0x11, false, false);
            PumpWait(30);
            SendKey(0x2D, false, true);
            PumpWait(30);
            SendKey(0x2D, true, true);
            SendKey(0x11, true, false);
        }

        private static string DescribeForeground()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "<无前台窗口>";
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                string proc = "<>";
                try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
                catch (Exception) { }
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                return proc + " | " + (sb.Length > 60 ? sb.ToString(0, 60) : sb.ToString());
            }
            catch (Exception ex) { return "<诊断失败 " + ex.Message + ">"; }
        }

        /// <summary>失败提示用：仅前台进程名（气球文案简洁）。</summary>
        private static string TargetProc()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "无前台窗口";
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch (Exception) { return "目标应用"; }
        }

        /// <summary>前台是否终端类宿主（分类规则见 TerminalGuard）。命中时
        /// 捕获路径绝不注入 Ctrl+C——终端里它会成为 SIGINT 杀掉运行中的任务。</summary>
        private static bool IsTerminalTarget(out string why)
        {
            why = null;
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                var cn = new System.Text.StringBuilder(256);
                GetClassName(hwnd, cn, 256);
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                string proc = null;
                try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
                catch (Exception) { }
                return TerminalGuard.IsTerminal(cn.ToString(), proc, out why);
            }
            catch (Exception) { return false; }
        }

        private static string CaptureCore(out string error)
        {
            error = null;
            object backup = null;
            try
            {
                // 捕获目标诊断：前台窗口进程（UIPI/管理员目标一眼可辨）+ 终端判定
                string termWhy;
                bool terminal = IsTerminalTarget(out termWhy);
                Program.LogHost("capture target: " + DescribeForeground()
                    + " focus: " + DescribeFocus()
                    + " terminal=" + (terminal ? termWhy : "no"));

                // P1：UIA 选区直读（不注入按键、不动剪贴板）——成功即翻译，
                // 剪贴板零污染；失败/无选区/超时一律无感落回下方剪贴板流程
                //（终端目标同样优先 UIA，是 TerminalGuard 之外的第二层防误杀）。
                // 缓存键=前台窗口 hwnd（负缓存：近期明确失败短期跳过 UIA）
                long uiaTarget = GetForegroundWindow().ToInt64();
                string uiaText, uiaWhy;
                if (UiaSelectionProvider.TryReadSelection(uiaTarget, out uiaText, out uiaWhy))
                    return uiaText;

                uint seq0 = GetClipboardSequenceNumber();
                try { backup = CloneClipboard(); } catch (Exception) { backup = null; }
                try { Clipboard.Clear(); } catch (Exception) { }
                Program.LogHost("capture diag: seq0=" + seq0 + " seq1=" + GetClipboardSequenceNumber()
                    + " backup=" + (backup != null));

                WaitForModifierRelease();
                uint seqStart = GetClipboardSequenceNumber();

                // 热键弦后的短窗口内 RIT 会吞掉注入按键（2026-09-02 实测：同注入
                // 无热键时正常、弦后全灭）——用递增延迟的尝试梯子探测吞键窗口：
                // 0ms/500ms/1300ms/2800ms。普通目标前三发 Ctrl+C、末发 Ctrl+Ins；
                // 终端目标全程只发 Ctrl+Ins（Ctrl+C 在终端=SIGINT 误杀运行任务）。
                int[] delays = { 0, 500, 1300, 2800 };
                string text = null;
                for (int i = 0; i < delays.Length && text == null; i++)
                {
                    if (delays[i] > 0) PumpWait(delays[i] - delays[i - 1]);
                    bool ctrlC = !terminal && i < delays.Length - 1;
                    Program.LogHost("capture attempt " + (i + 1) + " (+" + delays[i]
                        + "ms, " + (ctrlC ? "ctrl+c" : "ctrl+ins")
                        + ", seq=" + GetClipboardSequenceNumber() + "): "
                        + DescribeForeground());
                    if (ctrlC) InjectCopy(); else InjectCtrlInsert();
                    text = PollClipboardText(600);
                }
                if (string.IsNullOrEmpty(text))
                {
                    Program.LogHost("capture diag: seqStart=" + seqStart + " -> " + GetClipboardSequenceNumber());
                    error = terminal
                        ? "终端窗口（" + TargetProc() + "）：已改用 Ctrl+Insert 仍未取到选中文本；为避免 Ctrl+C 误中断运行中的任务，未注入 Ctrl+C"
                        : "未能获取选中文本（" + TargetProc() + " 未响应复制）";
                    RestoreClipboard(backup);
                    return null;
                }
                RestoreClipboard(backup);
                return text;
            }
            catch (Exception ex)
            {
                error = "选中捕获失败：" + ex.Message;
                try { RestoreClipboard(backup); } catch (Exception) { }
                return null;
            }
        }

        private static string PollClipboardText(int timeoutMs)
        {
            long ticks = DateTime.UtcNow.Ticks + timeoutMs * 10000L;
            bool loggedExc = false;
            while (DateTime.UtcNow.Ticks < ticks)
            {
                PumpWait(30);
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                }
                catch (Exception ex)
                {
                    if (!loggedExc) { Program.LogHost("clipboard read 异常: " + ex.Message); loggedExc = true; }
                    /* 剪贴板被占用：下轮再试 */
                }
            }
            return null;
        }

        /// <summary>全格式备份（AHK ClipboardAll 等价：枚举格式逐一复制）。</summary>
        private static object CloneClipboard()
        {
            IDataObject src = Clipboard.GetDataObject();
            if (src == null) return null;
            DataObject copy = new DataObject();
            foreach (string fmt in src.GetFormats())
            {
                try { copy.SetData(fmt, src.GetData(fmt)); }
                catch (Exception) { /* 个别格式不可序列化：跳过 */ }
            }
            return copy;
        }

        private static void RestoreClipboard(object backup)
        {
            var copy = backup as DataObject;
            if (copy == null) return;
            var deadline = DateTime.UtcNow.AddMilliseconds(800);
            while (DateTime.UtcNow < deadline)
            {
                try { Clipboard.SetDataObject(copy, true); return; }
                catch (Exception) { System.Threading.Thread.Sleep(40); }
            }
        }
    }

    // ------------------------------------------------------------
    // 托盘 + 全局热键
    // ------------------------------------------------------------
    public sealed class TrayController : IDisposable
    {
        public sealed class Deps
        {
            public Func<AppConfig> GetConfig;
            public Action TranslateSelection;               // 全局热键触发
            public Action TranslateShot;                    // 截图翻译热键/菜单（优化 5）
            public Action TranslateInput;                   // 输入翻译热键/菜单（优化 6）
            public Action OpenSettings, OpenCapture, OpenConfigPage;
            public Action ToggleProvider;                    // 提供商行点击（AHK ToggleProvider 语义）
            public Action<string> SetSourceLang;             // 子菜单选择
            public Action<string> SetTargetLang;
            public Action Exit;
            public Action<string> Log;
        }

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0xB00B;
        private const int HOTKEY_ID_SHOT = 0xB00C;   // 截图翻译热键（优化 5）
        private const int HOTKEY_ID_INPUT = 0xB00D;  // 输入翻译热键（优化 6）

        private readonly Deps _deps;
        private TrayForm _form;          // 隐藏窗体：NotifyIcon 宿主 + WM_HOTKEY 接收
        private uint _hkMods, _hkVk;     // 当前已注册的翻译热键
        private string _registeredHk;
        private string _shotRegisteredHk;
        private string _inputRegisteredHk;

        public TrayController(Deps deps) { _deps = deps; }

        public bool Headless { get; set; }

        /// <summary>进入独立模式：显示托盘 + 注册翻译热键 + 截图热键 + 输入热键。
        /// 返回翻译热键是否注册成功（截图/输入热键失败仅降级：托盘菜单仍可用）。</summary>
        public bool EnterStandalone()
        {
            if (Headless) return false;
            EnsureForm();
            _form.VisibleTray = true;
            bool ok = RegisterTranslateHotkey(_deps.GetConfig().Hotkey);
            string shot = _deps.GetConfig().ShotHotkey;
            if (!RegisterShotHotkey(shot))
                _deps.Log("shot hotkey 未注册（托盘菜单入口仍可用）: " + shot);
            string input = _deps.GetConfig().InputHotkey;
            if (!RegisterInputHotkey(input))
                _deps.Log("input hotkey 未注册（托盘菜单入口仍可用）: " + input);
            return ok;
        }

        /// <summary>注册输入翻译热键（AHK 串如 "^!i"）。失败返回 false。</summary>
        public bool RegisterInputHotkey(string hk)
        {
            UnregisterInputHotkey();
            if (Headless || string.IsNullOrEmpty(hk) || _form == null)
                return false;
            uint mods, vk;
            if (!ParseHotkey(hk, out mods, out vk))
            {
                _deps.Log("input hotkey 解析失败: " + hk);
                return false;
            }
            if (!RegisterHotKey(_form.Handle, HOTKEY_ID_INPUT, mods | 0x4000, vk)) // MOD_NOREPEAT
            {
                _deps.Log("input hotkey 注册失败（被占用？）: " + hk + " err=" + Marshal.GetLastWin32Error());
                return false;
            }
            _inputRegisteredHk = hk;
            _deps.Log("input hotkey registered: " + hk);
            return true;
        }

        public void UnregisterInputHotkey()
        {
            if (_form != null && _inputRegisteredHk != null)
            {
                UnregisterHotKey(_form.Handle, HOTKEY_ID_INPUT);
                _inputRegisteredHk = null;
            }
        }

        /// <summary>注册截图翻译热键（AHK 串如 "^!z"）。失败返回 false。</summary>
        public bool RegisterShotHotkey(string hk)
        {
            UnregisterShotHotkey();
            if (Headless || string.IsNullOrEmpty(hk) || _form == null)
                return false;
            uint mods, vk;
            if (!ParseHotkey(hk, out mods, out vk))
            {
                _deps.Log("shot hotkey 解析失败: " + hk);
                return false;
            }
            if (!RegisterHotKey(_form.Handle, HOTKEY_ID_SHOT, mods | 0x4000, vk)) // MOD_NOREPEAT
            {
                _deps.Log("shot hotkey 注册失败（被占用？）: " + hk + " err=" + Marshal.GetLastWin32Error());
                return false;
            }
            _shotRegisteredHk = hk;
            _deps.Log("shot hotkey registered: " + hk);
            return true;
        }

        public void UnregisterShotHotkey()
        {
            if (_form != null && _shotRegisteredHk != null)
            {
                UnregisterHotKey(_form.Handle, HOTKEY_ID_SHOT);
                _shotRegisteredHk = null;
            }
        }

        /// <summary>注册翻译热键（AHK 串如 "^!d"）。失败返回 false（被占用/非法）。</summary>
        public bool RegisterTranslateHotkey(string hk)
        {
            UnregisterTranslateHotkey();
            if (Headless || string.IsNullOrEmpty(hk) || _form == null)
                return false;
            uint mods, vk;
            if (!ParseHotkey(hk, out mods, out vk))
            {
                _deps.Log("hotkey 解析失败: " + hk);
                return false;
            }
            if (!RegisterHotKey(_form.Handle, HOTKEY_ID, mods | 0x4000, vk)) // MOD_NOREPEAT
            {
                _deps.Log("hotkey 注册失败（被占用？）: " + hk + " err=" + Marshal.GetLastWin32Error());
                return false;
            }
            _hkMods = mods; _hkVk = vk; _registeredHk = hk;
            _deps.Log("hotkey registered: " + hk + " (standalone)");
            return true;
        }

        public void UnregisterTranslateHotkey()
        {
            if (_form != null && _registeredHk != null)
            {
                UnregisterHotKey(_form.Handle, HOTKEY_ID);
                _registeredHk = null;
            }
        }

        public void Balloon(string title, string text)
        {
            if (_form == null || !_form.VisibleTray) return;
            try { _form.Balloon(title, text); } catch (Exception) { }
        }

        public void RefreshMenu()
        {
            if (_form != null && _form.VisibleTray)
                _form.BuildMenu();
        }

        private void EnsureForm()
        {
            if (_form != null) return;
            _form = new TrayForm(this);
        }

        private sealed class TrayForm : Form
        {
            private readonly TrayController _owner;
            private readonly NotifyIcon _icon;
            private readonly ContextMenuStrip _menu;

            public TrayForm(TrayController owner)
            {
                _owner = owner;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                Opacity = 0;
                _menu = new ContextMenuStrip();
                _icon = new NotifyIcon
                {
                    Text = "translator",
                    Icon = LoadIcon(),
                    ContextMenuStrip = _menu,
                    Visible = false
                };
                _icon.DoubleClick += delegate { _owner._deps.OpenSettings(); };
                // 左键同样弹菜单（AHK 单击托盘即出菜单的惯例）
                _icon.MouseUp += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left && _icon.Visible)
                    {
                        BuildMenu();
                        var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        if (mi != null) mi.Invoke(_icon, null);
                        else _menu.Show(Cursor.Position);
                    }
                };
            }

            private static Icon LoadIcon()
            {
                // 优先 exe 自身嵌入图标（ApplicationIcon，便携包无需外部 ico 文件）
                try
                {
                    Icon embedded = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    if (embedded != null) return embedded;
                }
                catch (Exception) { }
                try
                {
                    // 开发布局兜底：scripts\icon.ico（与旧 translator.exe 同源）
                    string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\icon.ico");
                    if (System.IO.File.Exists(p)) return new Icon(p, 16, 16);
                }
                catch (Exception) { }
                return SystemIcons.Application;
            }

            public bool VisibleTray
            {
                get { return _icon.Visible; }
                set { _icon.Visible = value; if (value) BuildMenu(); }
            }

            public void Balloon(string title, string text)
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.ShowBalloonTip(2000);
            }

            /// <summary>按当前配置重建菜单（逐项对齐 AHK UpdateMenuLabels）。</summary>
            public void BuildMenu()
            {
                AppConfig cfg = _owner._deps.GetConfig();

                _menu.Items.Clear();
                // 设置… = Default 项（双击托盘触发，A_TrayMenu.Default 语义）
                var settings = new ToolStripMenuItem("设置…")
                {
                    Font = new Font(SystemFonts.MenuFont, FontStyle.Bold)
                };
                settings.Click += delegate { _owner._deps.OpenSettings(); };
                _menu.Items.Add(settings);

                // 当前热键：信息行（AHK 回调为空操作）
                var hk = new ToolStripMenuItem("当前热键：" + CaptureManager.FormatHotkey(cfg.Hotkey))
                {
                    Enabled = false
                };
                _menu.Items.Add(hk);

                // 翻译提供商：整行点击 = 切换（ToggleProvider 语义：已配置项循环）
                var prov = new ToolStripMenuItem("翻译提供商：" + ProviderCatalog.DisplayName(cfg.Provider));
                prov.Click += delegate { _owner._deps.ToggleProvider(); };
                _menu.Items.Add(prov);

                // 语言子菜单：全表按定义序（gLangs 顺序），勾选当前；目标语言不含自动检测
                _menu.Items.Add(BuildLangMenu("源语言", cfg.SourceLang, true));
                _menu.Items.Add(BuildLangMenu("目标语言", cfg.TargetLang, false));

                // 截图翻译：动作项，标签带当前热键（发现性）
                var shot = new ToolStripMenuItem("截图翻译（"
                    + CaptureManager.FormatHotkey(cfg.ShotHotkey) + "）");
                shot.Click += delegate
                {
                    if (_owner._deps.TranslateShot != null) _owner._deps.TranslateShot();
                };
                _menu.Items.Add(shot);

                // 输入翻译：动作项（优化 6）
                var input = new ToolStripMenuItem("输入翻译（"
                    + CaptureManager.FormatHotkey(cfg.InputHotkey) + "）");
                input.Click += delegate
                {
                    if (_owner._deps.TranslateInput != null) _owner._deps.TranslateInput();
                };
                _menu.Items.Add(input);

                // 配置百度密钥…（AHK 菜单无此行，但 ToggleProvider 错误文案引用它——补齐更实用）
                var config = new ToolStripMenuItem("配置百度密钥…");
                config.Click += delegate { _owner._deps.OpenConfigPage(); };
                _menu.Items.Add(config);

                _menu.Items.Add(new ToolStripSeparator());
                var exit = new ToolStripMenuItem("退出");
                exit.Click += delegate { _owner._deps.Exit(); };
                _menu.Items.Add(exit);
            }

            /// <summary>语言子菜单：LanguageTable.Order 顺序（= gLangs 定义序），勾选当前。</summary>
            private ToolStripMenuItem BuildLangMenu(string title, string current, bool allowAuto)
            {
                var root = new ToolStripMenuItem(title + "：" + LanguageTable.DisplayName(current));
                foreach (string id in LanguageTable.Order)
                {
                    if (!allowAuto && LanguageTable.IsAuto(id)) continue;
                    var item = new ToolStripMenuItem(LanguageTable.DisplayName(id)) { Checked = id == current };
                    string captured = id;
                    item.Click += delegate
                    {
                        if (title == "源语言")
                            _owner._deps.SetSourceLang(captured);
                        else
                            _owner._deps.SetTargetLang(captured);
                    };
                    root.DropDownItems.Add(item);
                }
                return root;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
                {
                    _owner._deps.TranslateSelection();
                    return;
                }
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID_SHOT)
                {
                    if (_owner._deps.TranslateShot != null) _owner._deps.TranslateShot();
                    return;
                }
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID_INPUT)
                {
                    if (_owner._deps.TranslateInput != null) _owner._deps.TranslateInput();
                    return;
                }
                base.WndProc(ref m);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _icon != null)
                {
                    _icon.Visible = false;
                    _icon.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>AHK 热键串 → MOD_* + VK（"^!d" → 0x2|0x1, 'D'）。</summary>
        public static bool ParseHotkey(string hk, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(hk)) return false;
            int i = 0;
            for (; i < hk.Length; i++)
            {
                char c = hk[i];
                if (c == '^') mods |= 0x2;            // MOD_CONTROL
                else if (c == '!') mods |= 0x1;       // MOD_ALT
                else if (c == '+') mods |= 0x4;       // MOD_SHIFT
                else if (c == '#') mods |= 0x8;       // MOD_WIN
                else if (c == '*') { /* 通配：RegisterHotKey 忽略 */ }
                else break;
            }
            string key = hk.Substring(i).Trim();
            if (key.Length == 0) return false;
            vk = KeyNameToVk(key);
            return vk != 0;
        }

        private static uint KeyNameToVk(string name)
        {
            string k = name.Length == 1 ? name.ToUpperInvariant() : name;
            switch (k)
            {
                case "BACKSPACE": return 0x08;
                case "TAB": return 0x09;
                case "ENTER": return 0x0D;
                case "PAUSE": return 0x13;
                case "CAPSLOCK": return 0x14;
                case "ESCAPE": case "ESC": return 0x1B;
                case "SPACE": return 0x20;
                case "PGUP": return 0x21;
                case "PGDN": return 0x22;
                case "END": return 0x23;
                case "HOME": return 0x24;
                case "LEFT": return 0x25;
                case "UP": return 0x26;
                case "RIGHT": return 0x27;
                case "DOWN": return 0x28;
                case "PRINTSCREEN": return 0x2C;
                case "INSERT": case "INS": return 0x2D;
                case "DELETE": case "DEL": return 0x2E;
                case "NUMLOCK": return 0x90;
                case "SCROLLLOCK": return 0x91;
                case "NUMPADMULT": return 0x6A;
                case "NUMPADADD": return 0x6B;
                case "NUMPADSUB": return 0x6D;
                case "NUMPADDOT": return 0x6E;
                case "NUMPADDIV": return 0x6F;
                case "`": return 0xC0;
                case ";": return 0xBA;
                case "=": return 0xBB;
                case ",": return 0xBC;
                case "-": return 0xBD;
                case ".": return 0xBE;
                case "/": return 0xBF;
                case "[": return 0xDB;
                case "\\": return 0xDC;
                case "]": return 0xDD;
                case "'": return 0xDE;
            }
            if (k.Length == 1)
            {
                char c = k[0];
                if (c >= '0' && c <= '9') return (uint)c;
                if (c >= 'A' && c <= 'Z') return (uint)c;
            }
            if ((k[0] == 'F' || k[0] == 'f') && k.Length >= 2)
            {
                int n;
                if (int.TryParse(k.Substring(1), out n) && n >= 1 && n <= 24)
                    return (uint)(0x70 + n - 1);
            }
            if (k.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (k.Length == 7 && int.TryParse(k.Substring(6), out n) && n >= 0 && n <= 9)
                    return (uint)(0x60 + n);
            }
            return 0;
        }

        public void Dispose()
        {
            UnregisterTranslateHotkey();
            UnregisterShotHotkey();
            UnregisterInputHotkey();
            if (_form != null) { _form.Dispose(); _form = null; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
