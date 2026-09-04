//=============================================================
// PageBusiness.cs - 页面级业务承接（阶段 3 建；阶段 6 起 AHK 退役，
// 所有窗口一律由宿主承接，无透传兜底）。
// 本类只做「消息编排」，翻译/配置/剪贴板实现全部在 Translator.Core
//（architecture.md 规则 1：Form/Program 内禁止业务实现）。
// 消息（docs/protocol.md §2）：
//   translate → Translator.Core 翻译 → result/error 帧（requestId 配对）
//   copy      → Translator.Core Clipboard 写剪贴板
//   close/drag/热键捕获/设置业务 → 宿主直处理
//=============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Translator.Core.Clipboard;
using Translator.Core.Configuration;
using Translator.Core.Infrastructure;
using Translator.Core.Providers;
using Translator.Core.Translation;

namespace TranslatorHost
{
    public class PageBusiness
    {
        private readonly Action<Action> _marshalToUi;
        private readonly Func<int, MainWindow> _windowLookup;

        private ConfigStore _config;
        private TranslationService _service;

        // 每窗口状态：init 缓存 + 取消源（取消源=窗口关闭/重试/宿主退出/管道断开）
        private readonly Dictionary<int, string> _pendingText = new Dictionary<int, string>();
        private readonly Dictionary<int, CancellationTokenSource> _inflight =
            new Dictionary<int, CancellationTokenSource>();

        // 阶段 5a：热键捕获（宿主侧状态机，替代 AHK CaptureRound）
        private CaptureManager _capture;
        // 待应用热键（captured → apply 之间暂存，按 winId 区分双窗并发）
        private readonly Dictionary<int, string> _pendingHk = new Dictionary<int, string>();

        // 阶段 5b：宿主自开窗（托盘/热键发起；桥 open_window 驱动的测试窗不含于此）
        private readonly Dictionary<int, string> _selfPages = new Dictionary<int, string>();
        public Action<string> SelfReapplyHotkey;   // (hk) → 宿主重注册翻译热键，成功 true
        public Action MenuRefresh;                 // 配置变更后刷新托盘菜单
        public Action<string, string> Notify;      // (title, text) 托盘气泡（AHK ShowToast 等价）

        public PageBusiness(Action<Action> marshalToUi, Func<int, MainWindow> windowLookup)
        {
            _marshalToUi = marshalToUi;
            _windowLookup = windowLookup;
        }

        /// <summary>C# 是否已接管业务（hello 带 configPath 且 ConfigStore 就绪）</summary>
        public bool OwnsBusiness { get { return _service != null; } }

        /// <summary>当前配置快照（托盘菜单/独立模式用；未接管返回默认空配置）</summary>
        public AppConfig CurrentConfig
        {
            get { return _config != null ? _config.Current : new AppConfig(); }
        }

        /// <summary>Core 初始化（启动时以独立模式配置路径调用一次）。
        /// 迁移：config.conf 中密钥仍为明文（无 dpapi: 前缀）时立即加密回写
        /// ——此后磁盘上永无明文密钥（优化 3）；回写失败不阻塞启动（下次
        /// 任一配置保存动作会再迁移）。</summary>
        public bool InitFromConfigPath(string configPath)
        {
            if (string.IsNullOrEmpty(configPath))
                return false;
            try
            {
                _config = new ConfigStore(configPath);
                _service = new TranslationService(_config);
                MigrateSecretsToDpapi();
                return true;
            }
            catch (Exception ex)
            {
                Program.LogHost("PageBusiness 初始化失败（配置未接管）: " + ex.Message);
                _config = null;
                _service = null;
                return false;
            }
        }

        /// <summary>密钥明文 → DPAPI 迁移（启动一次性；幂等——已加密即跳过）。
        /// 只记迁移是否发生，不记密钥内容。</summary>
        private void MigrateSecretsToDpapi()
        {
            try
            {
                string file = File.ReadAllText(_config.FilePath, Encoding.UTF8);
                bool plain = (file.IndexOf("baidu_appid=", StringComparison.Ordinal) >= 0
                              && file.IndexOf("baidu_appid=" + SecretProtector.Prefix, StringComparison.Ordinal) < 0)
                          || (file.IndexOf("baidu_secret=", StringComparison.Ordinal) >= 0
                              && file.IndexOf("baidu_secret=" + SecretProtector.Prefix, StringComparison.Ordinal) < 0);
                if (!plain) return;
                if (_config.WriteSync(_config.Current))   // WriteSyncCore 恒加密落盘
                    Program.LogHost("config 密钥已迁移 DPAPI 加密（明文行已消除）");
                else
                    Program.LogHost("config 密钥迁移回写失败（磁盘/权限），下次保存时重试");
            }
            catch (Exception ex)
            {
                Program.LogHost("config 密钥迁移检查失败: " + ex.GetType().Name);
            }
        }

        // ---------- 页面消息承接（返回 true = 已消费） ----------

        public bool HandlePageEvent(int winId, string envJson)
        {
            var env = JsonUtil.ParseObject(envJson);
            if (env == null)
                return false;
            string type = JsonUtil.GetString(env, "type");
            if (type == "copy")
                return HandleCopy(env);
            // ui_event 宿主统一记日志（诊断锚点）；未知事件名静默忽略
            if (type == "ui_event")
            {
                var evArgs = JsonUtil.GetList(env, "payload");
                string ev = evArgs != null && evArgs.Count > 0 ? evArgs[0] as string : null;
                Program.LogHost("ui_event winId=" + winId + (ev != null ? " " + ev : ""));
                return true;
            }
            int rid = JsonUtil.GetInt(env, "requestId");
            bool self = IsSelfWindow(winId);
            switch (type)
            {
                case "translate":
                    return HandleTranslate(winId, rid);
                // close/drag：宿主所有窗口一律自己处理（阶段 6 起无 AHK 兜底）
                case "close":
                    CloseWindowById(winId);
                    return true;
                case "drag":
                {
                    // 原生拖动不可用的旧运行时上：页面发 drag → 宿主接管拖动
                    MainWindow dw = _windowLookup(winId);
                    if (dw != null) dw.BeginDrag();
                    return true;
                }
                // capture 页 ready：宿主启动捕获；init 只由宿主推给自开窗
                //（桥驱动的测试窗 init 由客户端自行 push，宿主知情即可）
                case "ready":
                    if (IsPage(winId, "capture"))
                    {
                        StartCapture(winId, rid, closeOnEsc: true);
                        if (self)
                            PushSelfInit(winId);
                        return true;
                    }
                    if (self)
                        return PushSelfInit(winId);
                    return true;
                // ---------- 阶段 5a：热键捕获消息流（宿主 CaptureManager 承接） ----------
                case "captureHotkey":       // settings 页内联捕获：Esc 只取消不关窗
                    return StartCapture(winId, rid, closeOnEsc: false);
                case "recapture":           // capture 页重新捕获：Esc 取消并关窗（legacy 语义）
                    return StartCapture(winId, rid, closeOnEsc: true);
                case "apply":               // capture 页应用：成功后关窗
                    return HandleApply(winId, rid, closeAfterApply: true);
                case "applyHotkey":         // settings 页应用：窗口保持
                    return HandleApply(winId, rid, closeAfterApply: false);
                case "cancel":              // capture 页取消：恢复 + 关窗
                    return HandleCancel(winId, rid, closeWindow: true);
                case "cancelCapture":       // settings 页取消：恢复 + captureCancelled
                    return HandleCancel(winId, rid, closeWindow: false);
                // ---------- 设置业务（自开窗宿主直处理；桥驱动测试窗不受理） ----------
                case "setLang":
                    return self && OwnsBusiness && HandleSetLang(env);
                case "setProvider":
                    return self && OwnsBusiness && HandleSetProvider(winId, rid, env);
                case "saveBaidu":
                    return self && OwnsBusiness && HandleSaveBaidu(winId, rid, env);
                case "saveDeepl":
                    return self && OwnsBusiness && HandleSaveDeepl(winId, rid, env);
                case "saveLlm":
                    return self && OwnsBusiness && HandleSaveLlm(winId, rid, env);
                case "openurl":
                {
                    // 域名白名单校验在宿主（仅自开窗受理）
                    var oArgs = JsonUtil.GetList(env, "payload");
                    string url = oArgs != null && oArgs.Count > 0 ? oArgs[0] as string : null;
                    if (self && IsAllowedUrl(url))
                    {
                        try { System.Diagnostics.Process.Start(url); } catch (Exception ex) { Program.LogHost("openurl 失败: " + ex.Message); }
                        return true;
                    }
                    return self;   // 自开窗非白名单 URL：吃掉不放行
                }
            }
            return false;
        }

        // ---------- 阶段 5b：宿主自开窗（托盘/热键发起） ----------

        /// <summary>登记宿主自开窗（Program 建窗后调用）。</summary>
        public void RegisterSelfWindow(int winId, string page)
        {
            lock (_selfPages) { _selfPages[winId] = page ?? ""; }
        }

        public void UnregisterSelfWindow(int winId)
        {
            lock (_selfPages) { _selfPages.Remove(winId); }
        }

        /// <summary>自开结果窗的待翻译文本（等价 AHK init push 的 srcText 缓存路径）。</summary>
        public void SetPendingText(int winId, string text)
        {
            lock (_pendingText) { _pendingText[winId] = text ?? ""; }
        }

        /// <summary>托盘菜单切语言（托盘事件在 UI 线程，无需 marshal）。</summary>
        public void SetLangSelf(string which, string id)
        {
            if (!OwnsBusiness || !LanguageTable.IsKnown(id))
                return;
            var next = _config.Current;
            if (which == "src") next.SourceLang = id;
            else if (LanguageTable.IsAuto(id)) return;
            else next.TargetLang = id;
            _config.WriteSync(next);
            if (MenuRefresh != null) MenuRefresh();
            Program.LogHost("tray setLang " + which + "=" + id);
        }

        /// <summary>托盘菜单切提供商；目标未配置时返回 false（气泡报错文案在 LastError）。</summary>
        public bool SetProviderSelf(string p)
        {
            if (!OwnsBusiness)
                return true;
            string missing = null;
            if (p == "baidu" && !_config.Current.HasBaiduKeys())
                missing = "尚未配置百度翻译密钥，请先在设置页完成配置";
            else if (p == "deepl" && !_config.Current.HasDeeplKey())
                missing = "尚未配置 DeepL API Key，请先在设置页完成配置";
            else if (p == "llm" && !_config.Current.HasLlmConfig())
                missing = "尚未配置 AI 大模型服务，请先在设置页完成配置";
            if (missing != null)
            {
                NoteError(missing);
                return false;
            }
            var next = _config.Current;
            next.Provider = p;
            _config.WriteSync(next);
            if (MenuRefresh != null) MenuRefresh();
            Program.LogHost("tray setProvider " + p);
            return true;
        }

        /// <summary>最近一次错误文案（托盘气泡显示用）。</summary>
        public string LastError { get; private set; }
        internal void NoteError(string msg) { LastError = msg; }

        /// <summary>托盘提供商行点击切换（AHK ToggleProvider 语义扩展）：
        /// 在「已配置可用」的 Provider 间循环（mymemory 恒可用；baidu/deepl/llm
        /// 需配置齐备才参与），从当前项顺延到下一个。</summary>
        public void ToggleProviderSelf()
        {
            string cur = CurrentConfig.Provider;
            // 可用环：mymemory 恒在；其余按配置就绪过滤
            var ring = new List<string> { "mymemory" };
            if (_config.Current.HasBaiduKeys()) ring.Add("baidu");
            if (_config.Current.HasDeeplKey()) ring.Add("deepl");
            if (_config.Current.HasLlmConfig()) ring.Add("llm");
            int idx = ring.IndexOf(cur);
            string target = ring[(idx + 1) % ring.Count];   // 当前不在环内（未配置项）→ 落 mymemory
            if (!SetProviderSelf(target))
            {
                if (Notify != null) Notify("translator", LastError);
                return;
            }
            if (Notify != null)
                Notify("translator", "翻译提供商已切换为：" + ProviderCatalog.DisplayName(target));
        }

        public bool IsSelfWindow(int winId)
        {
            lock (_selfPages) { return _selfPages.ContainsKey(winId); }
        }

        private string SelfPage(int winId)
        {
            lock (_selfPages)
            {
                string p;
                return _selfPages.TryGetValue(winId, out p) ? p : null;
            }
        }

        /// <summary>可复用的结果窗：最近打开且未关闭的自开 result 窗（遗留优化：
        /// 翻译窗未关时再次划词，在同一窗口刷新而不是再开一个）。0 = 无。</summary>
        public int FindReusableResultWindow()
        {
            lock (_selfPages)
            {
                int best = 0;
                foreach (var kv in _selfPages)
                    if (kv.Value == "result" && kv.Key > best) best = kv.Key;
                return best;
            }
        }

        /// <summary>在既有结果窗中换文本重翻：更新待译文本 → 重推 init（页面
        /// init 处理器会重置状态并补发 translate）→ 窗口置前。</summary>
        public void ReuseResultWindow(int winId, string text)
        {
            SetPendingText(winId, text);
            PushSelfInit(winId);
            MainWindow win = _windowLookup(winId);
            if (win != null && !win.IsDisposed)
            {
                try { win.Activate(); } catch (Exception) { }
            }
            Program.LogHost("复用结果窗 winId=" + winId);
        }

        /// <summary>自开窗 init 推送（settings/result/capture/config 四页；载荷与 protocol.md §3 一致）。</summary>
        public bool PushSelfInit(int winId)
        {
            if (!OwnsBusiness)
                return false;
            string page = SelfPage(winId);
            if (page == null)
                return false;
            MainWindow win = _windowLookup(winId);
            bool ndrag = win != null ? win.NativeDrag : true;
            var cfg = _config.Current;
            string payload = null;
            if (page == "settings")
            {
                // settings init：热键键帽 + 常用置顶语言表（对齐 AHK SettingsInitJson）
                // 优化 4：新增 deepl/llm 配置字段（页面按所选 Provider 条件渲染配置卡）
                payload = JsonUtil.Serialize(new Dictionary<string, object>
                {
                    { "hotkey", CaptureManager.FormatHotkey(cfg.Hotkey) },
                    { "hotkeyKeys", CaptureManager.KeysJson(cfg.Hotkey) },
                    { "src", cfg.SourceLang },
                    { "tgt", cfg.TargetLang },
                    { "provider", cfg.Provider },
                    { "hasKeys", !string.IsNullOrEmpty(cfg.BaiduAppid) && !string.IsNullOrEmpty(cfg.BaiduSecret) },
                    { "appid", cfg.BaiduAppid ?? "" },
                    { "secret", cfg.BaiduSecret ?? "" },
                    { "deeplKey", cfg.DeeplKey ?? "" },
                    { "deeplEndpoint", cfg.DeeplEndpoint ?? "" },
                    { "llmPreset", string.IsNullOrEmpty(cfg.LlmPreset) ? "custom" : cfg.LlmPreset },
                    { "llmBaseUrl", cfg.LlmBaseUrl ?? "" },
                    { "llmApiKey", cfg.LlmApiKey ?? "" },
                    { "llmModel", cfg.LlmModel ?? "" },
                    { "llmPrompt", cfg.LlmPrompt ?? "" },
                    { "ndrag", ndrag },
                    { "langs", BuildLangsList() }
                });
            }
            else if (page == "result")
            {
                // result init：设置集中化（遗留项）——结果窗头部齿轮 Popover 复用
                // settings 组件，需要同一份 langs/热键/语言选择数据（AHK 推的 init 无
                // 这些字段，页面按「缺失 = 关闭设置入口」降级，不影响 AHK 双轨）。
                string text;
                lock (_pendingText) { _pendingText.TryGetValue(winId, out text); }
                payload = JsonUtil.Serialize(new Dictionary<string, object>
                {
                    { "srcText", text ?? "" },
                    { "srcLangLabel", cfg.SourceLang == "auto" ? "AUTO" : cfg.SourceLang.ToUpperInvariant() },
                    { "tgtLangLabel", cfg.TargetLang.ToUpperInvariant() },
                    { "provider", ProviderCatalog.DisplayName(cfg.Provider) },
                    { "providerKey", cfg.Provider },
                    { "ndrag", ndrag },
                    
                    { "hotkey", CaptureManager.FormatHotkey(cfg.Hotkey) },
                    { "hotkeyKeys", CaptureManager.KeysJson(cfg.Hotkey) },
                    { "langs", BuildLangsList() },
                    { "src", cfg.SourceLang },
                    { "tgt", cfg.TargetLang },
                    { "hasKeys", !string.IsNullOrEmpty(cfg.BaiduAppid) && !string.IsNullOrEmpty(cfg.BaiduSecret) }
                });
            }
            else if (page == "capture")
            {
                payload = JsonUtil.Serialize(new Dictionary<string, object>
                {
                    { "cur", CaptureManager.FormatHotkey(cfg.Hotkey) },
                    { "ndrag", ndrag }
                });
            }
            else if (page == "config")
            {
                payload = JsonUtil.Serialize(new Dictionary<string, object>
                {
                    { "appid", cfg.BaiduAppid ?? "" },
                    { "secret", cfg.BaiduSecret ?? "" },
                    { "ndrag", ndrag }
                });
            }
            if (payload == null)
                return false;
            Push(winId, Protocol.PageEnvelope("init", payload, 0));
            Program.LogHost("self init winId=" + winId + " page=" + page);
            return true;
        }

        /// <summary>常用置顶语言表（settings 与 result 共用；对齐 AHK SettingsInitJson 形状）。</summary>
        private static List<object> BuildLangsList()
        {
            var langs = new List<object>();
            var pinned = new List<string> { "auto", "zh-CN", "en", "ja", "ko" };
            var rest = new List<string>();
            foreach (string id in LanguageTable.Order)
                if (!pinned.Contains(id)) rest.Add(id);
            rest.Sort((a, b) => string.Compare(LanguageTable.DisplayName(a),
                LanguageTable.DisplayName(b), StringComparison.OrdinalIgnoreCase));
            foreach (string pid in pinned)
            {
                if (pid == "auto" || LanguageTable.DisplayName(pid) != null)
                    langs.Add(new Dictionary<string, object>
                    {
                        { "id", pid },
                        { "name", LanguageTable.DisplayName(pid) },
                        { "auto", pid == "auto" },
                        { "common", true }
                    });
            }
            foreach (string rid in rest)
                langs.Add(new Dictionary<string, object>
                {
                    { "id", rid },
                    { "name", LanguageTable.DisplayName(rid) },
                    { "common", true }
                });
            return langs;
        }

        private bool HandleSetLang(Dictionary<string, object> env)
        {
            var args = JsonUtil.GetList(env, "payload");
            if (args == null || args.Count < 2)
            {
                Program.LogHost("setLang 拒绝：payload 参数不足 "
                    + (args == null ? "(null)" : args.Count.ToString()));
                return true;
            }
            string which = args[0] as string, id = args[1] as string;
            if (!LanguageTable.IsKnown(id) || (which != "src" && which != "tgt"))
            {
                Program.LogHost("setLang 拒绝：非法参数 which=" + (which ?? "null")
                    + " id=" + (id ?? "null"));
                return true;
            }
            var next = _config.Current;
            if (which == "src") next.SourceLang = id;
            else if (LanguageTable.IsAuto(id)) return true;   // 目标语言不允许 auto
            else next.TargetLang = id;
            _config.WriteSync(next);
            if (MenuRefresh != null) MenuRefresh();
            Program.LogHost("setLang " + which + "=" + id + " (self)");
            return true;
        }

        private bool HandleSetProvider(int winId, int rid, Dictionary<string, object> env)
        {
            var args = JsonUtil.GetList(env, "payload");
            string p = args != null && args.Count > 0 ? args[0] as string : null;
            if (p != "baidu" && p != "mymemory" && p != "deepl" && p != "llm")
                return true;
            // 门禁：切换到需配置的 Provider 时校验前置（错误帧 code=provider_not_ready，
            // 页面把焦点引导到对应配置卡）
            string missing = null;
            if (p == "baidu" && !_config.Current.HasBaiduKeys())
                missing = "尚未配置百度翻译密钥";
            else if (p == "deepl" && !_config.Current.HasDeeplKey())
                missing = "尚未配置 DeepL API Key";
            else if (p == "llm" && !_config.Current.HasLlmConfig())
                missing = "尚未配置 AI 大模型的 Base URL 和模型";
            if (missing != null)
            {
                PushPageError(winId, rid, "provider_not_ready", missing);
                return true;
            }
            var next = _config.Current;
            next.Provider = p;
            _config.WriteSync(next);
            if (MenuRefresh != null) MenuRefresh();
            Push(winId, Protocol.PageEnvelope("providerUpdated", JsonUtil.Serialize(
                new Dictionary<string, object> { { "provider", p } }), rid));
            Program.LogHost("setProvider " + p + " (self)");
            return true;
        }

        private bool HandleSaveDeepl(int winId, int rid, Dictionary<string, object> env)
        {
            var args = JsonUtil.GetList(env, "payload");
            string key = args != null && args.Count > 0 ? args[0] as string : null;
            string endpoint = args != null && args.Count > 1 ? args[1] as string : null;
            if (string.IsNullOrEmpty(key))
            {
                PushPageError(winId, rid, "save_failed", "DeepL API Key 不能为空");
                return true;
            }
            var next = _config.Current;
            next.DeeplKey = key.Trim();
            next.DeeplEndpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint.Trim();
            next.Provider = "deepl";
            if (!_config.WriteSync(next))
            {
                PushPageError(winId, rid, "save_failed", "配置写盘失败");
                return true;
            }
            if (MenuRefresh != null) MenuRefresh();
            Push(winId, Protocol.PageEnvelope("deeplSaved", JsonUtil.Serialize(
                new Dictionary<string, object> { { "ok", true } }), rid));
            Program.LogHost("saveDeepl ok (self) keyLen=" + key.Trim().Length);
            return true;
        }

        private bool HandleSaveLlm(int winId, int rid, Dictionary<string, object> env)
        {
            var args = JsonUtil.GetList(env, "payload");
            string preset = args != null && args.Count > 0 ? args[0] as string : null;
            string baseUrl = args != null && args.Count > 1 ? args[1] as string : null;
            string apiKey = args != null && args.Count > 2 ? args[2] as string : null;
            string model = args != null && args.Count > 3 ? args[3] as string : null;
            string prompt = args != null && args.Count > 4 ? args[4] as string : null;
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(model))
            {
                PushPageError(winId, rid, "save_failed", "Base URL 和模型名不能为空");
                return true;
            }
            var next = _config.Current;
            next.LlmPreset = string.IsNullOrEmpty(preset) ? "custom" : preset;
            next.LlmBaseUrl = baseUrl.Trim();
            next.LlmApiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey.Trim();
            next.LlmModel = model.Trim();
            next.LlmPrompt = string.IsNullOrEmpty(prompt) ? null : prompt.Trim();
            next.Provider = "llm";
            if (!_config.WriteSync(next))
            {
                PushPageError(winId, rid, "save_failed", "配置写盘失败");
                return true;
            }
            if (MenuRefresh != null) MenuRefresh();
            Push(winId, Protocol.PageEnvelope("llmSaved", JsonUtil.Serialize(
                new Dictionary<string, object> { { "ok", true } }), rid));
            Program.LogHost("saveLlm ok (self) preset=" + next.LlmPreset
                + " model=" + next.LlmModel + " keyLen=" + (next.LlmApiKey ?? "").Length);
            return true;
        }

        private bool HandleSaveBaidu(int winId, int rid, Dictionary<string, object> env)
        {
            var args = JsonUtil.GetList(env, "payload");
            string appid = args != null && args.Count > 0 ? args[0] as string : null;
            string secret = args != null && args.Count > 1 ? args[1] as string : null;
            if (string.IsNullOrEmpty(appid) || string.IsNullOrEmpty(secret))
            {
                PushPageError(winId, rid, "save_failed", "APP ID 和密钥不能为空");
                return true;
            }
            var next = _config.Current;
            next.BaiduAppid = appid.Trim();
            next.BaiduSecret = secret.Trim();
            next.Provider = "baidu";
            if (!_config.WriteSync(next))
            {
                PushPageError(winId, rid, "save_failed", "配置写盘失败");
                return true;
            }
            if (MenuRefresh != null) MenuRefresh();
            Push(winId, Protocol.PageEnvelope("baiduSaved", JsonUtil.Serialize(
                new Dictionary<string, object> { { "ok", true } }), rid));
            Program.LogHost("saveBaidu ok (self) appidLen=" + appid.Trim().Length);
            return true;
        }

        /// <summary>openurl 域名白名单（仅百度翻译开放平台；Program/页面共用）。</summary>
        public static bool IsAllowedUrl(string url)
        {
            return url != null && url.StartsWith("https://", StringComparison.Ordinal)
                && url.IndexOf("fanyi-api.baidu.com", StringComparison.Ordinal) == 8;
        }

        // ---------- 阶段 5a：热键捕获编排 ----------

        public void AttachCapture(CaptureManager capture) { _capture = capture; }

        private bool IsPage(int winId, string page)
        {
            MainWindow w = _windowLookup(winId);
            return w != null && w.Page == page;
        }

        /// <summary>捕获拦截当前热键用（宿主持有 config 值；未接管返回 null=不拦截）。</summary>
        public string CurrentHotkeyForCapture()
        {
            return OwnsBusiness ? _config.Current.Hotkey : null;
        }

        /// <summary>捕获完成回调（CaptureManager UI 线程回调）：暂存待应用键。</summary>
        public void RecordCaptured(int winId, string hk, List<string> keys)
        {
            lock (_pendingHk) { _pendingHk[winId] = hk; }
        }

        private bool StartCapture(int winId, int rid, bool closeOnEsc)
        {
            try
            {
                if (!_capture.Start(winId, closeOnEsc))
                {
                    PushPageError(winId, rid, "hotkey_busy", "热键捕获已在进行中");
                }
            }
            catch (Exception ex)
            {
                PushPageError(winId, rid, "hotkey_invalid", "捕获初始化失败：" + ex.Message);
            }
            return true;
        }

        private bool HandleApply(int winId, int rid, bool closeAfterApply)
        {
            string hk;
            lock (_pendingHk) { _pendingHk.TryGetValue(winId, out hk); }
            if (string.IsNullOrEmpty(hk))
            {
                PushPageError(winId, rid, "no_capture", "尚未捕获到热键");
                return true;
            }
            ApplyHotkeyCore(winId, hk, closeAfterApply);
            return true;
        }

        /// <summary>应用热键核心：同键仅推 hotkeyUpdated；异键写盘 + 宿主重注册
        /// 托盘热键；capture 页（closeAfterApply=true）应用后关窗（legacy 行为）。</summary>
        private void ApplyHotkeyCore(int winId, string hk, bool closeAfterApply)
        {
            if (!OwnsBusiness)
            {
                PushPageError(winId, 0, "save_failed", "配置未就绪，热键未更改");
                return;
            }
            string cur = _config.Current.Hotkey;
            if (hk != cur)
            {
                var next = _config.Current;
                next.Hotkey = hk;
                if (!_config.WriteSync(next))
                {
                    Program.LogHost("热键写盘失败 hk=" + hk);
                    PushPageError(winId, 0, "save_failed", "配置写盘失败，热键未更改");
                    return;
                }
                Program.LogHost("hotkey 承接 winId=" + winId + " " + cur + " -> " + hk);
            }
            else
            {
                Program.LogHost("hotkey 未变化 winId=" + winId + " hk=" + hk);
            }
            if (SelfReapplyHotkey != null)
            {
                try { SelfReapplyHotkey(hk); } catch (Exception ex) { Program.LogHost("hotkey 重注册失败: " + ex.Message); }
            }
            Push(winId, Protocol.PageEnvelope("hotkeyUpdated", JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "hk", hk },
                { "keys", CaptureManager.KeysJson(hk) }
            }), 0));
            if (closeAfterApply)
                CloseWindowById(winId);   // capture 页：应用后关窗（legacy 行为）
        }

        private bool HandleCancel(int winId, int rid, bool closeWindow)
        {
            if (_capture != null)
                _capture.Cancel(winId);
            lock (_pendingHk) { _pendingHk.Remove(winId); }
            if (closeWindow)
                CloseWindowById(winId);   // 旧语义：capture 页取消 = 恢复并关窗（无 captureCancelled 帧）
            else
                Push(winId, Protocol.PageEnvelope("captureCancelled", null, rid));
            return true;
        }

        private void PushPageError(int winId, int requestId, string code, string message)
        {
            Push(winId, Protocol.PageEnvelope("error", JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "code", code },
                { "message", message }
            }), requestId));
            Program.LogHost("page error winId=" + winId + " code=" + code + " msg=" + message);
        }

        private void CloseWindowById(int winId)
        {
            MainWindow win = _windowLookup(winId);
            if (win != null && !win.IsDisposed)
            {
                try { win.Close(); } catch (Exception) { }
            }
        }

        private bool HandleCopy(Dictionary<string, object> env)
        {
            // STA UI 线程调用（PageEvent 本就派发在 UI 线程）
            var args = JsonUtil.GetList(env, "payload");
            string text = args != null && args.Count > 0 ? args[0] as string : null;
            if (string.IsNullOrEmpty(text))
                return true;
            try
            {
                ClipboardService.SetText(text);
            }
            catch (Exception ex)
            {
                Program.LogHost("copy 失败: " + ex.Message); // 剪贴板被占用等；页面不感知
            }
            return true;
        }

        private bool HandleTranslate(int winId, int requestId)
        {
            if (!OwnsBusiness)
            {
                // Core 未就绪（配置初始化失败）：无 AHK 兜底，明确回错误帧
                PushError(winId, requestId, "翻译核心未初始化（配置读取失败）");
                return true;
            }

            string text;
            lock (_pendingText) { _pendingText.TryGetValue(winId, out text); }
            Program.LogHost("translate 承接 winId=" + winId + " rid=" + requestId
                + " textLen=" + (text == null ? -1 : text.Length));

            if (string.IsNullOrEmpty(text))
            {
                PushError(winId, requestId, "没有待翻译文本");
                return true;
            }

            // 重试/重复请求：取消上一次（协议取消源之一）
            CancellationTokenSource old;
            lock (_inflight)
            {
                if (_inflight.TryGetValue(winId, out old))
                {
                    try { old.Cancel(); } catch (ObjectDisposedException) { }
                }
                var ctsNew = new CancellationTokenSource();
                _inflight[winId] = ctsNew;
                RunTranslate(winId, requestId, text, ctsNew);
            }
            return true;
        }

        private void RunTranslate(int winId, int requestId, string text, CancellationTokenSource cts)
        {
            var req = new TranslationRequest { Text = text };
            var service = _service;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    var r = service.TranslateAsync(req, cts.Token).GetAwaiter().GetResult();
                    _marshalToUi(delegate
                    {
                        if (IsLatest(winId, cts))
                            PushResult(winId, requestId, r);
                    });
                }
                catch (OperationCanceledException)
                {
                    // 取消源：窗口关闭/重试/断开 —— 静默
                }
                catch (TranslateException tex)
                {
                    _marshalToUi(delegate
                    {
                        if (IsLatest(winId, cts))
                            PushError(winId, requestId, tex.Message);
                    });
                }
                catch (Exception ex)
                {
                    _marshalToUi(delegate
                    {
                        if (IsLatest(winId, cts))
                            PushError(winId, requestId, "翻译失败：" + ex.Message);
                    });
                }
            }, cts.Token, TaskCreationOptions.None, TaskScheduler.Default);
        }

        private bool IsLatest(int winId, CancellationTokenSource cts)
        {
            lock (_inflight)
            {
                CancellationTokenSource cur;
                return _inflight.TryGetValue(winId, out cur) && ReferenceEquals(cur, cts);
            }
        }

        // ---------- 页面信封组装（协议 §4 统一模型 / §3 error 帧） ----------

        private void PushResult(int winId, int requestId, TranslationResult r)
        {
            string payload = JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "sourceText", r.SourceText },
                { "translatedText", r.TranslatedText },
                { "sourceLanguage", r.SourceLanguage },
                { "targetLanguage", r.TargetLanguage },
                { "provider", r.Provider },
                { "elapsedMs", r.ElapsedMs }
            });
            Push(winId, Protocol.PageEnvelope("result", payload, requestId));
            Program.LogHost("translate 完成 winId=" + winId + " provider=" + r.Provider
                + " elapsedMs=" + r.ElapsedMs + " len=" + (r.TranslatedText ?? "").Length);
        }

        private void PushError(int winId, int requestId, string message)
        {
            string payload = JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "code", Protocol.ETranslateFailed },
                { "message", message }
            });
            Push(winId, Protocol.PageEnvelope("error", payload, requestId));
        }

        private void Push(int winId, string pageEnvelopeJson)
        {
            MainWindow win = _windowLookup(winId);
            if (win != null && !win.IsDisposed)
                win.Push(pageEnvelopeJson);
        }

        // ---------- 生命周期清理 ----------

        public void CancelWindow(int winId)
        {
            // 捕获会话联动：窗口关闭时终止该窗口的捕获（取消源之一）
            if (_capture != null)
            {
                try { _capture.Cancel(winId); } catch (Exception) { }
            }
            lock (_pendingHk) { _pendingHk.Remove(winId); }
            lock (_selfPages) { _selfPages.Remove(winId); }
            CancellationTokenSource cts;
            lock (_inflight) { if (_inflight.TryGetValue(winId, out cts)) _inflight.Remove(winId); }
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                try { cts.Dispose(); } catch (ObjectDisposedException) { }
            }
            lock (_pendingText) { _pendingText.Remove(winId); }
        }

        public void CancelAll()
        {
            List<int> ids;
            lock (_inflight) { ids = new List<int>(_inflight.Keys); }
            foreach (int id in ids)
                CancelWindow(id);
        }
    }
}
