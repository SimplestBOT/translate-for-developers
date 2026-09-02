//=============================================================
// HotkeyCapture.cs - 宿主侧热键捕获状态机（阶段 5a，修复遗留问题 2）
// 替代 AHK CaptureRound（InputHook 单实例跨轮状态残留 → 二次捕获失效）：
//   - 每轮捕获 = 一个新 Session（钩子/定时器/待应用键全部随会话创建销毁），
//     结束即彻底 Dispose，无任何跨轮残留状态。
//   - WH_KEYBOARD_LL 观察组合键（修饰键按住 + 非修饰键按下 = 一轮捕获完成），
//     WH_MOUSE_LL 捕获鼠标侧键 XButton1/2（按下即完成）。
//   - 捕获期间吃掉「当前翻译热键」组合与侧键（拦截旧 AHK 捕获期停用热键的职责，
//     避免捕获中误触发翻译）；吃掉 Esc 并按 closeOnEsc 语义收尾。
//   - 30s 无输入超时 → captureCancelled（旧实现无超时，为改进项）。
//   - 完成后 pending 暂存，apply 由 PageBusiness 走 ConfigStore 落盘 +
//     桥 C→A hotkey_changed（AHK 重注册翻译热键并应答 ack）。
// 线程模型：钩子与 Timer 全部创建于 UI 线程（Application.Run 消息泵），
// 回调自然回到 UI 线程，无需 marshal。
//=============================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Translator.Core.Infrastructure;

namespace TranslatorHost
{
    public sealed class CaptureManager : IDisposable
    {
        public sealed class Deps
        {
            public Action<int, string> PushToPage;      // (winId, pageEnvelopeJson)
            public Action<int> CloseWindow;             // 关闭窗口（capture 页 Esc/应用后）
            public Func<string> CurrentHotkey;          // config.Current.Hotkey（捕获期拦截用）
            public Action<int, string, List<string>> OnCaptured; // (winId, hk, keys)
            public Action<string> Log;
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_XBUTTONDOWN = 0x020B;

        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        private const int VK_ESCAPE = 0x1B;

        private readonly Deps _deps;
        private Session _active;

        private sealed class Session : IDisposable
        {
            public int WinId;
            public bool CloseOnEsc;
            public IntPtr KbHook, MouseHook;
            public Timer Timeout;

            public void Dispose()
            {
                if (KbHook != IntPtr.Zero) { UnhookWindowsHookEx(KbHook); KbHook = IntPtr.Zero; }
                if (MouseHook != IntPtr.Zero) { UnhookWindowsHookEx(MouseHook); MouseHook = IntPtr.Zero; }
                if (Timeout != null) { Timeout.Stop(); Timeout.Dispose(); Timeout = null; }
            }
        }

        public CaptureManager(Deps deps) { _deps = deps; }

        /// <summary>开始一轮捕获。false = 已有进行中的捕获（hotkey_busy）。</summary>
        public bool Start(int winId, bool closeOnEsc)
        {
            if (_active != null)
            {
                _deps.Log("capture busy：会话进行中 winId=" + _active.WinId);
                return false;
            }
            var s = new Session { WinId = winId, CloseOnEsc = closeOnEsc };
            IntPtr hMod = Marshal.GetHINSTANCE(typeof(CaptureManager).Module);
            s.KbHook = SetWindowsHookEx(WH_KEYBOARD_LL, KbProc, hMod, 0);
            s.MouseHook = SetWindowsHookEx(WH_MOUSE_LL, MouseProc, hMod, 0);
            if (s.KbHook == IntPtr.Zero || s.MouseHook == IntPtr.Zero)
            {
                s.Dispose();
                _deps.Log("capture 钩子安装失败 kb=" + s.KbHook + " mouse=" + s.MouseHook);
                throw new Exception("键盘钩子安装失败（GetLastError=" + Marshal.GetLastWin32Error() + "）");
            }
            s.Timeout = new Timer { Interval = 30000 };
            Timer t = s.Timeout;
            s.Timeout.Tick += delegate { CancelActive(); OnCancelled(s.WinId, closeWindow: false); };
            s.Timeout.Start();
            _active = s;
            _deps.PushToPage(winId, Protocol.PageEnvelope("capturing", null, 0));
            _deps.Log("capture start winId=" + winId + " closeOnEsc=" + (closeOnEsc ? "true" : "false"));
            return true;
        }

        /// <summary>取消进行中的捕获。返回是否有活动会话。</summary>
        public bool Cancel(int winId)
        {
            Session s = _active;
            if (s == null || s.WinId != winId)
                return false;
            CancelActive();
            return true;
        }

        private void CancelActive()
        {
            Session s = _active;
            if (s == null) return;
            _active = null;
            s.Dispose();
        }

        private void OnCancelled(int winId, bool closeWindow)
        {
            if (closeWindow)
                _deps.CloseWindow(winId);   // 旧语义：capture 页 Esc = 取消并关窗
            else
                _deps.PushToPage(winId, Protocol.PageEnvelope("captureCancelled", null, 0));
        }

        // ---------- 钩子 ----------

        private IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || _active == null)
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            int msg = wParam.ToInt32();
            if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN)
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            var kbs = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            uint vk = kbs.vkCode;
            if (IsModifier(vk))
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            if (vk == VK_ESCAPE)
            {
                // Esc = 取消捕获（吃掉按键：不透传页面，避免 settings 页 Esc 处理器重复触发）
                bool closeEsc = _active.CloseOnEsc;
                int winId = _active.WinId;
                CancelActive();
                _deps.Log("capture esc winId=" + winId);
                OnCancelled(winId, closeEsc);
                return (IntPtr)1;
            }
            // 当前翻译热键按下：吃掉（等价旧实现捕获期停用热键），捕获继续
            string combo = BuildCombo(vk, out _, out _);
            if (IsCurrentHotkey(combo))
                return (IntPtr)1;
            CompleteCapture(vk, 0);
            return (IntPtr)1;
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || _active == null)
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            if (wParam.ToInt32() != WM_XBUTTONDOWN)
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            var ms = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            int btn = (int)((ms.mouseData >> 16) & 0xFFFF);
            if (btn == 1 || btn == 2)
            {
                CompleteCapture(0, btn);   // XButton1/2：按下即完成（旧实现等待抬起，立即完成等价）
                return (IntPtr)1;
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void CompleteCapture(uint vk, int mouseBtn)
        {
            Session s = _active;
            if (s == null) return;
            CancelActive();
            string hk;
            List<string> keys;
            if (mouseBtn == 1) { hk = "XButton1"; keys = KeysJson(hk); }
            else if (mouseBtn == 2) { hk = "XButton2"; keys = KeysJson(hk); }
            else
            {
                string keyName;
                hk = BuildCombo(vk, out keyName, out keys);
            }
            _deps.OnCaptured(s.WinId, hk, keys);
            _deps.PushToPage(s.WinId, Protocol.PageEnvelope("captured",
                JsonUtil.Serialize(new Dictionary<string, object> { { "hk", hk }, { "keys", keys } }), 0));
            _deps.Log("capture 完成 winId=" + s.WinId + " hk=" + hk);
        }

        // ---------- 键码/热键串 ----------

        /// <summary>修饰键状态（GetAsyncKeyState 高位）+ vk → AHK 热键串（如 "^!k"）与键帽数组。</summary>
        private string BuildCombo(uint vk, out string keyName, out List<string> keys)
        {
            bool ctrl = KeyDown(VK_CONTROL), alt = KeyDown(VK_MENU), shift = KeyDown(VK_SHIFT);
            bool win = KeyDown(VK_LWIN) || KeyDown(VK_RWIN);
            string name = VkToName(vk);
            keyName = name;
            string hk = (ctrl ? "^" : "") + (alt ? "!" : "") + (shift ? "+" : "") + (win ? "#" : "") + name;
            keys = KeysJson(hk);
            return hk;
        }

        private static bool KeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        private static bool IsModifier(uint vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14
                || vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3
                || vk == 0xA4 || vk == 0xA5 || vk == VK_LWIN || vk == VK_RWIN;
        }

        private bool IsCurrentHotkey(string combo)
        {
            Func<string> f = _deps.CurrentHotkey;
            string cur = f != null ? f() : null;
            if (string.IsNullOrEmpty(cur) || string.IsNullOrEmpty(combo))
                return false;
            return combo.TrimStart('*').ToLowerInvariant() == cur.TrimStart('*').ToLowerInvariant();
        }

        /// <summary>vk → AHK 键名（US 布局约定，对齐 AHK InputHook EndKey 命名）。</summary>
        private static string VkToName(uint vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return ((char)('0' + (vk - 0x30))).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)('A' + (vk - 0x41))).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Numpad" + (vk - 0x60);
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            switch (vk)
            {
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x13: return "Pause";
                case 0x14: return "CapsLock";
                case 0x1B: return "Escape";
                case 0x20: return "Space";
                case 0x21: return "PgUp";
                case 0x22: return "PgDn";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x6A: return "NumpadMult";
                case 0x6B: return "NumpadAdd";
                case 0x6D: return "NumpadSub";
                case 0x6E: return "NumpadDot";
                case 0x6F: return "NumpadDiv";
                case 0x90: return "NumLock";
                case 0x91: return "ScrollLock";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "``";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
            }
            return "";
        }

        /// <summary>AHK KeysJson 等价：热键串 → 键帽标签数组（["Ctrl","Alt","K"]）。</summary>
        public static List<string> KeysJson(string hk)
        {
            var arr = new List<string>();
            if (hk.IndexOf('^') >= 0) arr.Add("Ctrl");
            if (hk.IndexOf('!') >= 0) arr.Add("Alt");
            if (hk.IndexOf('+') >= 0) arr.Add("Shift");
            if (hk.IndexOf('#') >= 0) arr.Add("Win");
            // 剥行首修饰符前缀。注意：类内 ^ 必须转义（[\^!+#]），否则 [^!+#]
            // 是否定类——把主键剥掉留下 "!H"（2026-09-02 键帽显示 bug 根因）
            string key = System.Text.RegularExpressions.Regex.Replace(hk, "^[\\^!+#]+", "");
            string label;
            switch (key)
            {
                case "XButton1": label = "侧键1"; break;
                case "XButton2": label = "侧键2"; break;
                case "Escape": label = "Esc"; break;
                case "Backspace": label = "Bksp"; break;
                case "Delete": label = "Del"; break;
                case "Insert": label = "Ins"; break;
                case "Up": label = "↑"; break;
                case "Down": label = "↓"; break;
                case "Left": label = "←"; break;
                case "Right": label = "→"; break;
                default: label = key.Length == 1 ? key.ToUpperInvariant() : key; break;
            }
            arr.Add(label);
            return arr;
        }

        /// <summary>AHK FormatHotkey 等价："^!d" → "Ctrl+Alt+D"（含侧键中文标签）。</summary>
        public static string FormatHotkey(string hk)
        {
            string outp = "";
            if (hk.IndexOf('^') >= 0) outp += "Ctrl+";
            if (hk.IndexOf('!') >= 0) outp += "Alt+";
            if (hk.IndexOf('+') >= 0) outp += "Shift+";
            if (hk.IndexOf('#') >= 0) outp += "Win+";
            // 同 KeysJson：类内 ^ 必须转义，见彼处注释
            string key = System.Text.RegularExpressions.Regex.Replace(hk, "^[\\^!+#]+", "");
            if (key == "XButton1") key = "鼠标侧键1";
            else if (key == "XButton2") key = "鼠标侧键2";
            else if (key == "Escape") key = "Esc";
            return outp + key;
        }

        public void Dispose()
        {
            Session s = _active;
            if (s != null) { _active = null; s.Dispose(); }
        }

        // ---------- Win32 ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr extraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public System.Drawing.Point pt; public uint mouseData; public uint flags; public uint time; public IntPtr extraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    }
}
