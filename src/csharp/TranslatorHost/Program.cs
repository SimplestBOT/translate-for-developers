//=============================================================
// Program.cs - C# 宿主入口（阶段 7 终版：纯独立模式，无桥）
// 职责：独立模式运行（托盘+全局热键+划词捕获+自开窗）；translate/
//       copy/热键捕获/设置业务由 PageBusiness 承接（业务实现在
//       Translator.Core，边界规则 1）。
// 支持参数：
//   --selftest   无头自检（WebView2 环境 + 配置读写冒烟），退出码 0/1
//   --open <page>[,text]   调试开窗（60s 安全阀）
//=============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Translator.Core.Configuration;

namespace TranslatorHost
{
    internal static class Program
    {
        // 实例名：默认 translator-bridge（历史名，Mutex 用）；TFD_PIPE_NAME
        // 仍可覆盖——用于隔离并行测试实例（Mutex 键，不再是管道名）
        private static readonly string PipeName =
            Environment.GetEnvironmentVariable("TFD_PIPE_NAME") ?? "translator-bridge";
        private static readonly Dictionary<int, MainWindow> Windows = new Dictionary<int, MainWindow>();
        private static int _nextId = 1;
        private static System.Windows.Forms.Control _marshaller;
        private static PageBusiness _business;
        private static CaptureManager _captureMgr;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest")
                return SelfTest();

            // 单实例：同管道名的宿主只允许一个（重复双击启动器=静默退出，日志留痕）
            bool mutexOwned;
            var mutex = new System.Threading.Mutex(true, "translator-tfd-host-" + PipeName, out mutexOwned);
            if (!mutexOwned)
            {
                LogHost("已有宿主实例在运行（管道 " + PipeName + "），本次启动退出");
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool headless = Environment.GetEnvironmentVariable("TFD_HEADLESS") == "1";
            // --open <page>[,text]：调试入口——独立模式直接开页（绕过 AHK 全链路）
            string openPage = null, openText = null;
            if (args.Length >= 2 && args[0] == "--open")
            {
                var seg = args[1].Split(new[] { ',' }, 2);
                openPage = seg[0];
                openText = seg.Length > 1 ? seg[1] : "The quick brown fox jumps over the lazy dog.";
                headless = false;
            }

            // 主线程隐藏控件：管道线程 → UI 线程的 marshal 通道
            // （WebView2 控件必须创建/操作于 STA 消息泵线程）
            _marshaller = new System.Windows.Forms.Control();
            _marshaller.CreateControl();
            _business = new PageBusiness(
                action => _marshaller.BeginInvoke(action),
                winId => { MainWindow w; return Windows.TryGetValue(winId, out w) ? w : null; });
            _captureMgr = new CaptureManager(new CaptureManager.Deps
            {
                PushToPage = delegate(int winId, string env)
                {
                    MainWindow w;
                    if (Windows.TryGetValue(winId, out w) && !w.IsDisposed)
                        w.Push(env);
                },
                CloseWindow = CloseWindow,
                CurrentHotkey = _business.CurrentHotkeyForCapture,
                OnCaptured = _business.RecordCaptured,
                Log = LogHost,
            });
            _business.AttachCapture(_captureMgr);

            // 阶段 5b：托盘 + 全局热键 + 选中捕获（独立模式）
            _tray = new TrayController(new TrayController.Deps
            {
                GetConfig = () => _business.CurrentConfig,
                TranslateSelection = delegate { OpenResultFromSelection(); },
                OpenSettings = delegate { OpenSelfWindow("settings", null); },
                OpenCapture = delegate { OpenSelfWindow("capture", null); },
                OpenConfigPage = delegate { OpenSelfWindow("config", null); },
                SetSourceLang = delegate(string id)
                {
                    _business.SetLangSelf("src", id);
                    _tray.Balloon("translator", "源语言已设为：" +
                        Translator.Core.Configuration.LanguageTable.DisplayName(id));
                },
                SetTargetLang = delegate(string id)
                {
                    _business.SetLangSelf("tgt", id);
                    _tray.Balloon("translator", "目标语言已设为：" +
                        Translator.Core.Configuration.LanguageTable.DisplayName(id));
                },
                ToggleProvider = delegate { _business.ToggleProviderSelf(); },
                Exit = delegate { Application.Exit(); },
                Log = LogHost,
            });
            _business.SelfReapplyHotkey = delegate(string hk)
            {
                if (!_tray.RegisterTranslateHotkey(hk))
                    LogHost("托盘热键重注册失败: " + hk);
            };
            _business.MenuRefresh = delegate { _tray.RefreshMenu(); };
            _business.Notify = delegate(string title, string text) { _tray.Balloon(title, text); };

            // 独立模式的配置初始化（测试客户端 hello.configPath 会覆盖重初始化）：
            // TFD_CONFIG 覆盖；默认 <base>\..\..\scripts\config.conf（bridge 目录布局）
            string cfgPath = Environment.GetEnvironmentVariable("TFD_CONFIG");
            if (string.IsNullOrEmpty(cfgPath))
            {
                string scriptsDir = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
                cfgPath = System.IO.Path.Combine(scriptsDir, "config.conf");
            }
            bool cfgOwned = _business.InitFromConfigPath(cfgPath);
            LogHost("standalone config " + (cfgOwned ? "已接管: " + cfgPath : "未接管: " + cfgPath));

            // 启动模式：纯独立（托盘+热键+划词捕获）——宿主常驻不退出。
            // TFD_HEADLESS=1（沙箱 e2e）：无托盘无热键。
            _tray.Headless = headless;
            if (openPage != null)
            {
                _openMode = true;
                _marshaller.BeginInvoke(new Action(delegate { OpenSelfWindow(openPage, openText); }));
                // --open 安全阀：60s 未关闭自动退出（正常路径由窗口关闭触发）
                var guard = new Timer { Interval = 60000 };
                guard.Tick += delegate { Application.Exit(); };
                guard.Start();
            }
            else
            {
                _marshaller.BeginInvoke(new Action(delegate
                {
                    if (_tray.EnterStandalone())
                    {
                        LogHost("standalone 启动（托盘+热键）");
                        _tray.Balloon("translator", "已就绪 · " +
                            CaptureManager.FormatHotkey(_business.CurrentConfig.Hotkey) + " 划词翻译");
                    }
                    else
                    {
                        LogHost("standalone 启动（热键注册失败，托盘可用）");
                        _tray.Balloon("translator",
                            "翻译热键注册失败（可能被占用）。\n如旧版 translator 正在运行请先退出，\n或到「更改热键」换一个组合。");
                    }
                }));
            }

            // 生命周期：宿主常驻独立运行；用户经托盘「退出」结束进程（Application.Exit）。
            // TFD_TEST_REUSE=1（调试）：开结果窗后 9s 驱动一次复用流程——
            // 绕过注入/热键直接验证「同窗换文本重翻」全链路（沙箱自动化用）。
            // TFD_TEST_REUSE=1（调试）：开结果窗后 9s 驱动一次复用流程——
            // 绕过注入/热键直接验证「同窗换文本重翻」全链路（沙箱自动化用）。
            if (Environment.GetEnvironmentVariable("TFD_TEST_REUSE") == "1")
            {
                _marshaller.BeginInvoke(new Action(delegate
                {
                    OpenSelfWindow("result", "first reuse test");
                    var t = new Timer { Interval = 9000 };
                    t.Tick += delegate
                    {
                        t.Stop();
                        int reuse = _business.FindReusableResultWindow();
                        LogHost("TEST reuse winId=" + reuse);
                        if (reuse != 0) _business.ReuseResultWindow(reuse, "second reuse test");
                    };
                    t.Start();
                }));
            }
            Application.Run(new ApplicationContext());
            GC.KeepAlive(mutex);   // 全程持有：防止 Mutex 被 GC 回收导致双实例
            return 0;
        }

        // ---------- 阶段 5b：独立模式入口 ----------

        private static TrayController _tray;
        private static bool _openMode;
        private static int _openWinId;

        /// <summary>全局热键 → 捕获选中文本 → 开结果窗（独立模式翻译主链路）。
        /// 捕获在后台执行（STA 专用线程，见 SelectionCapture），完成后回 UI 线程开窗。</summary>
        private static void OpenResultFromSelection()
        {
            System.Threading.Tasks.Task.Run(delegate
            {
                string err;
                string text = SelectionCapture.CaptureSelectedText(out err);
                var m = _marshaller;
                if (m == null || m.IsDisposed) return;
                m.BeginInvoke(new Action(delegate
                {
                    if (text == null)
                    {
                        LogHost("选中捕获失败: " + err);
                        _tray.Balloon("translator", err ?? "未能获取选中文本");
                        return;
                    }
                    // 遗留优化：已有未关闭的结果窗 → 同窗刷新（换文本重推 init + 置前）
                    int reuse = _business.FindReusableResultWindow();
                    if (reuse != 0)
                    {
                        _business.ReuseResultWindow(reuse, text);
                        return;
                    }
                    OpenSelfWindow("result", text);
                }));
            });
        }

        /// <summary>宿主自开窗：磁盘 HTML（webui\dist 优先，回退 AHK html 目录/内嵌解包）。</summary>
        private static readonly Dictionary<string, DateTime> _lastSelfOpen =
            new Dictionary<string, DateTime>();

        private static void OpenSelfWindow(string page, string text)
        {
            try
            {
                // 防抖：托盘双击会连带触发两次事件，500ms 内重复开同一页忽略
                //（result 页豁免——每次划词都要独立开窗）
                if (page != "result")
                {
                    lock (_lastSelfOpen)
                    {
                        DateTime last;
                        if (_lastSelfOpen.TryGetValue(page, out last)
                            && (DateTime.UtcNow - last).TotalMilliseconds < 500)
                        {
                            LogHost("开窗防抖：忽略重复 " + page);
                            return;
                        }
                        _lastSelfOpen[page] = DateTime.UtcNow;
                    }
                }
                string html = ResolvePageHtml(page);
                if (html == null)
                {
                    LogHost("自开窗失败：页面资源缺失 " + page);
                    return;
                }
                int winId = _nextId++;
                int w = 590, h = 540, pos = 1; // pos: 1=Center 2=NearCursor
                string title = "translator";
                switch (page)
                {
                    case "result": title = "翻译结果 - translator"; w = 590; h = 540; pos = 2; break;
                    case "capture": title = "更改翻译热键 - translator"; w = 440; h = 330; break;
                    case "config": title = "配置百度翻译 - translator"; w = 490; h = 470; break;
                    case "settings": title = "设置 - translator"; w = 480; h = 620; break;
                }
                var win = new MainWindow(winId, title, w, h, page);
                Windows[winId] = win;
                _business.RegisterSelfWindow(winId, page);
                if (page == "result" && !string.IsNullOrEmpty(text))
                    _business.SetPendingText(winId, text);
                if (_openMode) _openWinId = winId;

                win.PageEvent += (id, envJson) =>
                {
                    try { if (_business.HandlePageEvent(id, envJson)) return; }
                    catch (Exception ex) { LogHost("PageEvent 承接异常: " + ex.Message); }
                    LogHost("自开窗消息未消费（丢弃）winId=" + id);
                };
                win.Failed += e =>
                {
                    Windows.Remove(winId);
                    _business.CancelWindow(winId);
                    LogHost("自开窗创建失败: " + e);
                };
                win.WindowClosed += id =>
                {
                    Windows.Remove(id);
                    _business.CancelWindow(id);
                    if (_openMode && id == _openWinId)
                        _marshaller.BeginInvoke(new Action(delegate { Application.Exit(); }));
                };

                string userData = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "translator-tfd", "webview2");
                win.Open(html, pos == 2 ? "NearCursor" : "Center", userData);
            }
            catch (Exception ex)
            {
                LogHost("OpenSelfWindow 异常: " + ex.Message);
            }
        }

        /// <summary>页面 HTML 解析：scripts\..\webui\dist\<p>.html → scripts\webui\dist\ →
        /// %TEMP%\tfd_html（构建产物直读；legacy html 目录已于阶段 7 删除）。</summary>
        private static string ResolvePageHtml(string page)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;   // ...\scripts\bridge\
            string scriptsDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(baseDir, ".."));
            string[] candidates = new string[]
            {
                System.IO.Path.Combine(scriptsDir, "..\\webui\\dist\\" + page + ".html"),
                System.IO.Path.Combine(scriptsDir, "webui\\dist\\" + page + ".html"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp", "tfd_html", page + ".html"),
            };
            foreach (string f in candidates)
            {
                try
                {
                    if (System.IO.File.Exists(f))
                        return System.IO.File.ReadAllText(f, System.Text.Encoding.UTF8);
                }
                catch (Exception) { }
            }
            return null;
        }

        private static int SelfTest()
        {
            try
            {
                // WebView2 运行时探测（不弹 UI）
                var ver = CoreWebView2Environment.GetAvailableBrowserVersionString();
                Console.WriteLine("webview2=" + ver);

                // 阶段 3：Core 配置读写冒烟（目标环境文件 I/O 校验）
                string tmp = Path.Combine(Path.GetTempPath(),
                    "tfd-selftest-" + Guid.NewGuid().ToString("N") + ".conf");
                var store = new ConfigStore(tmp);
                if (!store.WriteSync(new AppConfig
                {
                    Hotkey = "^!d", Provider = "baidu",
                    SourceLang = "en", TargetLang = "ja",
                    BaiduAppid = "selftest-appid", BaiduSecret = "selftest-secret"
                }))
                    throw new Exception("config 写入失败");
                var back = new ConfigStore(tmp);
                if (back.Current.Provider != "baidu" || back.Current.TargetLang != "ja"
                    || back.Current.BaiduSecret != "selftest-secret")
                    throw new Exception("config 回读不一致");
                // 优化 3：密钥落盘必须为 DPAPI 密文（无明文）
                if (!File.ReadAllText(tmp).Contains("baidu_secret=" + SecretProtector.Prefix))
                    throw new Exception("config 密钥未加密落盘");
                File.Delete(tmp);
                Console.WriteLine("config=ok (dpapi-encrypted)");

                // P1 UIA Provider 冒烟（隔离子进程；结果环境相关，不计 PASS/FAIL，
                // 只验证 子进程启动→超时/返回→解析 全链路不炸宿主）。连续两次
                // 同键调用验证负缓存：若首查为确定性失败，第二查应 skip(cooldown)
                string uiaText, uiaWhy;
                bool uiaHit = UiaSelectionProvider.TryReadSelection(0xDEAD, out uiaText, out uiaWhy);
                Console.WriteLine("uia-provider=" + (uiaHit ? "hit(len=" + uiaText.Length + ")" : "miss(" + uiaWhy + ")"));
                if (!uiaHit)
                {
                    string t2, w2;
                    bool hit2 = UiaSelectionProvider.TryReadSelection(0xDEAD, out t2, out w2);
                    Console.WriteLine("uia-provider-2nd=" + (hit2 ? "hit" : "miss(" + w2 + ")"));
                }

                Console.WriteLine("SELFTEST PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST FAIL: " + ex.Message);
                return 1;
            }
        }

        /// <summary>宿主侧诊断日志（%TEMP%\tfd_host_err.log；不记录密钥与正文内容）</summary>
        public static void LogHost(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "tfd_host_err.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " [host] " + msg + "\r\n");
            }
            catch (Exception) { }
        }

        /// <summary>关窗并做业务清理（CaptureManager.Deps 注入用：窗口关闭即销毁捕获会话）。</summary>
        private static void CloseWindow(int winId)
        {
            MainWindow win;
            if (!Windows.TryGetValue(winId, out win)) return;
            Windows.Remove(winId);
            _business.CancelWindow(winId);
            try { win.Close(); } catch (Exception) { }
        }
    }
}
