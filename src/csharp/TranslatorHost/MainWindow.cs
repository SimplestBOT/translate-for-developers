//=============================================================
// MainWindow.cs - C# 宿主窗口（阶段 1）
// 职责边界（architecture.md 规则 1）：仅 Window lifecycle /
// WebView2 / DWM 效果 / 置顶 / 定位；禁止承载翻译、Provider、
// 配置、协议业务——那些属于 Translator.Core（阶段 3）与 AHK（Legacy）。
//=============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TranslatorHost
{
    public class MainWindow : Form
    {
        private readonly WebView2 _web;
        private bool _nativeDrag;
        private readonly int _winId;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>页面消息（winId, envelopeJson 字符串）</summary>
        public event Action<int, string> PageEvent;
        /// <summary>窗口就绪/失败（payload JSON，含 ok/ndrag/error）</summary>
        public event Action<string> ReadyReported;
        public event Action<int> WindowClosed;
        /// <summary>创建失败（async 内异常上抛给 Program）</summary>
        public event Action<string> Failed;

        /// <summary>页面类型（result/capture/config/settings/...，Program 建窗时记录）</summary>
        public string Page { get; private set; }

        /// <summary>宿主是否启用原生非客户区拖动（页面 init.ndrag 用）</summary>
        public bool NativeDrag { get { return _nativeDrag; } }

        // 旧运行时（无非客户区拖动）的 drag 消息拖动：释放鼠标捕获 + 模拟标题栏按下
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        /// <summary>页面 drag 消息：进入系统标题栏拖动循环。</summary>
        public void BeginDrag()
        {
            try
            {
                ReleaseCapture(Handle);
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            catch (Exception) { }
        }

        public MainWindow(int winId, string title, int w, int h, string page)
        {
            _winId = winId;
            Page = page ?? "";
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            // DPI（遗留问题 1）：宿主 manifest 声明 PerMonitorV2 后，WebView2 内容按
            // 屏 DPI 自缩放；这里只把 AHK 的逻辑尺寸按屏 DPI（96 基准）放大，
            // 使窗口物理大小与系统其他应用一致。GDI LOGPIXELSX = 主屏 DPI
            //（多屏异 DPI 由 PerMonitorV2 的 WM_DPICHANGED 路径兜底，此处为主值）。
            ClientSize = ScaleForDpi(new Size(w, h));
            BackColor = Color.FromArgb(0x0A, 0x0D, 0x13);

            _web = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(0x0A, 0x0D, 0x13),
            };
            _web.WebMessageReceived += OnWebMessage;
            Controls.Add(_web);
        }

        private const int LOGPIXELSX = 88;

        private static Size ScaleForDpi(Size logical)
        {
            try
            {
                int dpi = GetDeviceCaps(GetDC(IntPtr.Zero), LOGPIXELSX);
                if (dpi <= 0) dpi = 96;
                return new Size(
                    (int)Math.Round(logical.Width * dpi / 96.0),
                    (int)Math.Round(logical.Height * dpi / 96.0));
            }
            catch (Exception)
            {
                return logical;
            }
        }

        /// <summary>初始化 WebView2、定位、导航并显示。失败经 Failed 事件上抛。</summary>
        public async void Open(string html, string pos, string userDataFolder)
        {
            try
            {
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    null, userDataFolder, null);
                await _web.EnsureCoreWebView2Async(env);

                var core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                try { core.Settings.AreBrowserAcceleratorKeysEnabled = false; } catch (Exception) { }
                // push 门闩：NavigationCompleted 前到达的 push 入队，完成后按序放行
                core.NavigationCompleted += (s, e) =>
                {
                    System.Collections.Generic.List<string> drain = null;
                    lock (_pushGate)
                    {
                        if (e.IsSuccess) _navCompleted = true;
                        if (_navCompleted && _pendingPush.Count > 0)
                        {
                            drain = new System.Collections.Generic.List<string>(_pendingPush);
                            _pendingPush.Clear();
                        }
                    }
                    if (drain != null)
                        foreach (var msg in drain) Push(msg);
                };
                // 原生非客户区拖动（Runtime 109+）：标题栏拖动由 OS 处理（根治轮询卡顿）
                try
                {
                    core.Settings.IsNonClientRegionSupportEnabled = true;
                    _nativeDrag = true;
                }
                catch (Exception)
                {
                    _nativeDrag = false;
                }

                int w = ClientSize.Width, h = ClientSize.Height;
                if (pos == "NearCursor")
                    PositionNearCursor(w, h);
                else
                    CenterToWorkArea(w, h);

                // 剥离 WS_MAXIMIZEBOX（固定尺寸防误最大化），复刻 AHK DwmRound
                const int GWL_STYLE = -16;
                int style = GetWindowLong(Handle, GWL_STYLE);
                SetWindowLong(Handle, GWL_STYLE, style & ~0x10000);
                try
                {
                    int pref = 2;                       // DWMWCP_ROUND
                    int color = 0x463E3A;               // 描边（COLORREF: BGR）
                    DwmSetWindowAttribute(Handle, 33, ref pref, 4);
                    DwmSetWindowAttribute(Handle, 34, ref color, 4);
                }
                catch (Exception) { }

                _web.NavigateToString(html);
                Show();
                Activate();
                var payload = "{\"winId\":" + _winId + ",\"ok\":true,\"ndrag\":"
                    + (_nativeDrag ? "true" : "false") + "}";
                var r = ReadyReported;
                if (r != null) r(payload);
            }
            catch (Exception ex)
            {
                var f = Failed;
                if (f != null) f(ex.Message);
                else Close();
            }
        }

        private void PositionNearCursor(int w, int h)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = Math.Max(wa.Left + 4, Math.Min(Cursor.Position.X + 14, wa.Right - w - 8));
            int y = Math.Max(wa.Top + 4, Math.Min(Cursor.Position.Y + 18, wa.Bottom - h - 8));
            Location = new Point(x, y);
        }

        private void CenterToWorkArea(int w, int h)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // 页面 postMessage 传的是字符串（JSON 文本），必须取原始字符串；
            // WebMessageAsJson 会再包一层引号导致对端解析成字符串而非对象
            var cb = PageEvent;
            if (cb != null)
                cb(_winId, e.TryGetWebMessageAsString());
        }

        /// <summary>向页面推送信封（msg 已是 JSON 对象文本）。
        /// 双通道：PostWebMessageAsJson（React 页监听 chrome.webview message）
        /// + ExecuteScriptAsync __recv 注入（legacy 页消费）。
        /// 导航完成前入队，完成后按序放行。</summary>
        public void Push(string envelopeJson)
        {
            if (IsDisposed) return;
            lock (_pushGate)
            {
                if (!_navCompleted)
                {
                    _pendingPush.Add(envelopeJson);
                    return;
                }
            }
            var core = _web.CoreWebView2;
            if (core == null)
            {
                Program.LogHost("push 失败：CoreWebView2 未就绪 winId=" + _winId);
                return;
            }
            try { core.PostWebMessageAsJson(envelopeJson); }
            catch (Exception ex) { Program.LogHost("PostWebMessage winId=" + _winId + ": " + ex.Message); }
            core.ExecuteScriptAsync("window.__recv&&window.__recv(" + envelopeJson + ")");
        }

        private readonly object _pushGate = new object();
        private bool _navCompleted;
        private readonly List<string> _pendingPush = new List<string>();

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            var cb = WindowClosed;
            if (cb != null)
                cb(_winId);
            base.OnFormClosed(e);
        }
    }
}
