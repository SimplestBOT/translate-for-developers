//=============================================================
// CaptureShot.cs - 截图翻译（优化 5，2026-09-05）
// 流程：热键/托盘 → 光标所在屏拍快照（CopyFromScreen；PMv2 感知进程下
//   Screen.Bounds/鼠标坐标=物理像素，与快照 1:1，无换算）→ 全屏遮罩窗
//  （快照打底 + 半透明暗罩 + 橡皮筋框选；松手/Enter 确认，Esc/右键取消）
//   → 裁剪快照区域 → Windows.Media.Ocr 识别（后台线程，进程内 WinRT——
//   OCR 走系统 RuntimeBroker 服务、输入是自己的位图，不连第三方进程，
//   与 UIA 子进程隔离的风险面不同）→ 识别文本复用划词链路（结果窗复用/
//   ResultCache/重试/降级全生效）。
// 防御：窗体物理 bounds 与期望偏差 >2px 时 SetWindowPos 直接纠偏（绕过
//   WinForms 对 Bounds 的任何 DPI 虚拟化）+ 日志取证。
// 调试：TFD_TEST_SHOT=1 → 3s 后自动以主屏中央 800×500 跑 OCR→开窗全链路
//  （跳过遮罩交互，沙箱自动化用），配套外部 8s 自动退出。
//=============================================================
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Translator.Core.Configuration;
using Translator.Core.Ocr;

namespace TranslatorHost
{
    public static class ShotController
    {
        private const int MinSel = 12;        // 选框小于此视为误触丢弃
        private const int OcrTimeoutMs = 8000;

        private static int _busy;

        /// <summary>入口（UI 线程调用；热键 WM_HOTKEY 与托盘菜单共用）。
        /// busy 锁覆盖遮罩交互阶段（模态期间嵌套消息泵仍会分发 WM_HOTKEY，
        /// 二次触发在此拒绝）；OCR 阶段异步进行，重复触发无共享资源竞争。</summary>
        public static void Start(Func<AppConfig> getConfig, Action<string> openResult,
            Action<string> balloon, Action<string> log, Action<Action> marshal, bool testMode)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                balloon("截图翻译已在进行中");
                return;
            }
            try
            {
                if (testMode)
                {
                    // 沙箱自动化：跳过遮罩，主屏中央 800×500 → OCR → 开窗
                    var b = Screen.PrimaryScreen.Bounds;
                    int rw = Math.Min(800, b.Width), rh = Math.Min(500, b.Height);
                    var sel = new Rectangle(b.X + (b.Width - rw) / 2, b.Y + (b.Height - rh) / 2, rw, rh);
                    Bitmap snap = Snapshot(b, log);
                    if (snap == null) { balloon("截屏失败（详见宿主日志）"); return; }
                    log("TEST shot screen=" + b + " region=" + sel);
                    Bitmap region = snap.Clone(sel, PixelFormat.Format32bppArgb);
                    snap.Dispose();
                    RunOcrAndOpen(region, getConfig().SourceLang, openResult, balloon, log, marshal);
                    return;
                }

                var screen = Screen.FromPoint(Cursor.Position).Bounds;
                Bitmap shot = Snapshot(screen, log);
                if (shot == null)
                {
                    balloon("截屏失败（远程桌面/安全桌面场景不支持）");
                    return;
                }
                Rectangle selRect;
                try
                {
                    using (var form = new ShotOverlayForm(shot, screen, log))
                    {
                        if (form.ShowDialog() != DialogResult.OK)
                        {
                            log("shot: cancel");
                            return;
                        }
                        selRect = form.Selected;
                    }
                }
                finally { }
                using (shot)
                {
                    Bitmap region2 = shot.Clone(selRect, PixelFormat.Format32bppArgb);
                    RunOcrAndOpen(region2, getConfig().SourceLang, openResult, balloon, log, marshal);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private static Bitmap Snapshot(Rectangle bounds, Action<string> log)
        {
            try
            {
                var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
                return bmp;
            }
            catch (Exception ex)
            {
                log("shot snapshot 失败: " + ex.Message);
                return null;
            }
        }

        private static void RunOcrAndOpen(Bitmap region, string srcLang, Action<string> openResult,
            Action<string> balloon, Action<string> log, Action<Action> marshal)
        {
            Task.Run(delegate
            {
                var oc = OcrService.Recognize(region, srcLang, OcrTimeoutMs);
                try { region.Dispose(); } catch (Exception) { }
                // 不记录识别文本（与「不记正文」原则一致），只记元数据
                log("shot ocr: ok=" + (oc.Ok ? 1 : 0) + " lang=" + (oc.LangTag ?? "-")
                    + " pre=" + (oc.Preprocess ?? "-")
                    + " len=" + (oc.Text == null ? 0 : oc.Text.Length)
                    + (oc.Ok ? "" : " reason=" + oc.FailReason));
                marshal(new Action(delegate
                {
                    if (!oc.Ok) { balloon("OCR 失败：" + oc.FailReason); return; }
                    if (string.IsNullOrWhiteSpace(oc.Text)) { balloon("截图区域未识别到文字"); return; }
                    openResult(oc.Text);
                }));
            });
        }

        // ------------------------------------------------------------
        // 全屏遮罩窗：快照打底 + 暗罩 + 橡皮筋框选
        // ------------------------------------------------------------
        private sealed class ShotOverlayForm : Form
        {
            private readonly Bitmap _snap;
            private readonly Action<string> _log;
            private readonly Rectangle _screenBounds;   // 物理（=快照坐标系）
            private Point _anchor;
            private bool _dragging;
            private Rectangle _sel;

            public Rectangle Selected;

            private static readonly Font TipFont = new Font("Microsoft YaHei UI", 9.5f);

            public ShotOverlayForm(Bitmap snap, Rectangle screenBounds, Action<string> log)
            {
                _snap = snap;
                _screenBounds = screenBounds;
                _log = log;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = screenBounds;      // PMv2 感知进程：赋值即设备像素（MainWindow 同模式）
                TopMost = true;
                ShowInTaskbar = false;
                Cursor = Cursors.Cross;
                DoubleBuffered = true;
                KeyPreview = true;
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                VerifyOrFixBounds();
                Activate();
            }

            /// <summary>物理 bounds 实测纠偏：WinForms 对 Bounds 的 DPI 虚拟化
            /// 会让遮罩错位/不满屏——偏差 >2px 时用 SetWindowPos 绕过重设。</summary>
            private void VerifyOrFixBounds()
            {
                var r = new RECT();
                if (!GetWindowRect(Handle, ref r)) return;
                var e = _screenBounds;
                if (Math.Abs(r.Left - e.Left) > 2 || Math.Abs(r.Top - e.Top) > 2
                    || Math.Abs(r.Right - e.Right) > 2 || Math.Abs(r.Bottom - e.Bottom) > 2)
                {
                    _log("shot overlay bounds 偏差: got(" + r.Left + "," + r.Top + ","
                        + r.Right + "," + r.Bottom + ") exp(" + e.Left + "," + e.Top + ","
                        + e.Right + "," + e.Bottom + ") → SetWindowPos 纠偏");
                    SetWindowPos(Handle, IntPtr.Zero, e.Left, e.Top, e.Width, e.Height,
                        0x0010 | 0x0004); // SWP_NOACTIVATE | SWP_NOZORDER
                    var r2 = new RECT();
                    if (GetWindowRect(Handle, ref r2))
                        _log("shot overlay 纠偏后: (" + r2.Left + "," + r2.Top + ","
                            + r2.Right + "," + r2.Bottom + ")");
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.DrawImage(_snap, 0, 0, _snap.Width, _snap.Height);
                using (var dark = new SolidBrush(Color.FromArgb(105, 8, 10, 14)))
                using (var full = new Region(new Rectangle(0, 0, _snap.Width, _snap.Height)))
                {
                    if (!_sel.IsEmpty) full.Exclude(_sel);   // 选框内露出原亮度
                    g.FillRegion(dark, full);
                }
                if (!_sel.IsEmpty)
                {
                    using (var pen = new Pen(Color.FromArgb(140, 200, 255), 1.5f))
                        g.DrawRectangle(pen, _sel.X - 1, _sel.Y - 1, _sel.Width + 2, _sel.Height + 2);
                    string tag = _sel.Width + " × " + _sel.Height;
                    SizeF sz = g.MeasureString(tag, TipFont);
                    float tx = _sel.X, ty = _sel.Y - sz.Height - 8;
                    if (ty < 2) ty = _sel.Y + _sel.Height + 8;           // 贴顶翻到框下方
                    if (tx + sz.Width > _snap.Width - 4) tx = _snap.Width - sz.Width - 4;
                    using (var bg = new SolidBrush(Color.FromArgb(190, 12, 14, 20)))
                        g.FillRectangle(bg, tx - 5, ty - 2, sz.Width + 10, sz.Height + 4);
                    using (var fg = new SolidBrush(Color.FromArgb(235, 235, 240)))
                        g.DrawString(tag, TipFont, fg, tx, ty);
                }
                string tip = "拖拽框选要翻译的区域 · Enter 确认 · Esc 取消";
                SizeF ts = g.MeasureString(tip, TipFont);
                float tpx = (_snap.Width - ts.Width) / 2f;
                using (var bg = new SolidBrush(Color.FromArgb(200, 12, 14, 20)))
                    g.FillRectangle(bg, tpx - 12, 10, ts.Width + 24, ts.Height + 10);
                using (var fg = new SolidBrush(Color.FromArgb(240, 240, 245)))
                    g.DrawString(tip, TipFont, fg, tpx, 15);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
                if (e.Button == MouseButtons.Left)
                {
                    _dragging = true;
                    _anchor = e.Location;
                    _sel = Rectangle.Empty;
                    Invalidate();
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!_dragging) return;
                _sel = RectFrom(_anchor, e.Location);
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left || !_dragging) return;
                _dragging = false;
                if (_sel.Width >= MinSel && _sel.Height >= MinSel) Confirm();
                else { _sel = Rectangle.Empty; Invalidate(); }   // 误触丢弃，可重拖
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
                if (keyData == Keys.Enter && !_sel.IsEmpty
                    && _sel.Width >= MinSel && _sel.Height >= MinSel) { Confirm(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            private void Confirm()
            {
                Selected = _sel;
                DialogResult = DialogResult.OK;
                Close();
            }

            private static Rectangle RectFrom(Point a, Point b)
            {
                return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            }

            [DllImport("user32.dll")]
            private static extern bool GetWindowRect(IntPtr hWnd, ref RECT rect);
            [DllImport("user32.dll")]
            private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
            [StructLayout(LayoutKind.Sequential)]
            private struct RECT { public int Left, Top, Right, Bottom; }
        }
    }
}
