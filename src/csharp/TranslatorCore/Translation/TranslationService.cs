//=============================================================
// Translation/TranslationService.cs - 翻译组合层（async 第一天）
// 硬约束（architecture.md 边界规则 5）：
//   Task<TranslationResult> TranslateAsync(TranslationRequest, CancellationToken)
//   取消源 = 窗口关闭 / 重试 / 宿主退出 / 管道断开（由宿主注入 CTS）
// UI 只消费统一 TranslationResult 模型，永不接触 Provider 原始 JSON。
// 组合策略（2026-09-03，优化 2）：
//   缓存优先（相同语言对+文本 5min 内直出，省免费额度）→ 主 Provider
//   指数退避重试（仅网络类瞬时错误；超时不重试——15s 已付出）→ 失败
//   降级备用 Provider（MyMemory↔百度互备；百度无密钥则无备用）→ 全败
//   组合报错（主错误在前，备用结果附注）。成功结果入缓存。
//   结果 Provider 字段随实际成功方返回，页面徽标可见降级事实。
//=============================================================
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Translator.Core.Configuration;
using Translator.Core.Providers;

namespace Translator.Core.Translation
{
    public sealed class TranslationRequest
    {
        public string Text;
    }

    /// <summary>协议统一模型（docs/protocol.md §4），序列化键为 camelCase</summary>
    public sealed class TranslationResult
    {
        public string SourceText;
        public string TranslatedText;
        public string SourceLanguage;
        public string TargetLanguage;
        public string Provider;
        public long ElapsedMs;
    }

    public sealed class TranslationService
    {
        /// <summary>同 Provider 最大尝试次数（含首次）；仅对可重试错误生效</summary>
        internal const int MaxAttempts = 3;

        /// <summary>指数退避间隔（第 2/3 次尝试前）；测试可置 {0,0} 消除等待</summary>
        internal static readonly int[] RetryBackoffMs = { 500, 1000 };

        private readonly Func<AppConfig> _configProvider;
        private readonly Func<AppConfig, string, ITranslationProvider> _providerFactory;
        private readonly ResultCache _cache;

        /// <summary>生产构造：从 ConfigStore 快照取配置（写方=宿主，每次翻译取当下值）</summary>
        public TranslationService(ConfigStore store) : this(delegate { return store.Current; }) { }

        /// <summary>测试构造：任意配置源</summary>
        public TranslationService(Func<AppConfig> configProvider) : this(configProvider, null, null) { }

        /// <summary>完整构造（internal 测试用）：可注入 Provider 工厂与缓存</summary>
        internal TranslationService(Func<AppConfig> configProvider,
            Func<AppConfig, string, ITranslationProvider> providerFactory, ResultCache cache)
        {
            if (configProvider == null) throw new ArgumentNullException("configProvider");
            _configProvider = configProvider;
            _providerFactory = providerFactory ?? DefaultFactory;
            _cache = cache ?? new ResultCache(TimeSpan.FromMinutes(5));
        }

        /// <summary>默认工厂：分发规则——provider=baidu 且密钥齐备 → 百度；
        /// deepl 且有 key → DeepL；llm 且 BaseUrl+Model 齐 → LLM；
        /// 其余（或门禁不过）一律 MyMemory（可用性兜底）。</summary>
        private static ITranslationProvider DefaultFactory(AppConfig cfg, string id)
        {
            if (id == "baidu")
                return cfg.HasBaiduKeys()
                    ? (ITranslationProvider)new BaiduProvider(cfg.BaiduAppid, cfg.BaiduSecret)
                    : null;
            if (id == "deepl")
                return cfg.HasDeeplKey()
                    ? (ITranslationProvider)new DeepLProvider(cfg.DeeplKey, cfg.DeeplEndpoint)
                    : null;
            if (id == "llm")
                return cfg.HasLlmConfig()
                    ? (ITranslationProvider)new OpenAICompatibleProvider(
                        cfg.LlmBaseUrl, cfg.LlmApiKey, cfg.LlmModel,
                        string.IsNullOrEmpty(cfg.LlmPrompt) ? ProviderCatalog.DefaultLlmPrompt : cfg.LlmPrompt)
                    : null;
            if (id == "mymemory")
                return new MyMemoryProvider();
            return null;
        }

        private static string ResolvePrimaryId(AppConfig cfg)
        {
            switch (cfg.Provider)
            {
                case "baidu": return cfg.HasBaiduKeys() ? "baidu" : "mymemory";
                case "deepl": return cfg.HasDeeplKey() ? "deepl" : "mymemory";
                case "llm": return cfg.HasLlmConfig() ? "llm" : "mymemory";
                default: return "mymemory";
            }
        }

        private static string ResolveFallbackId(AppConfig cfg, string primaryId)
        {
            // 降级链：MyMemory（免费无门槛）→ 百度（有密钥时）→ 高质量付费
            //（DeepL/LLM 也可作主）。主=MyMemory 时只有百度可兜底。
            if (primaryId == "mymemory") return cfg.HasBaiduKeys() ? "baidu" : null;
            return "mymemory";
        }

        /// <summary>可重试错误 = 网络类瞬时错误（连接失败/限流/服务端 5xx/一般
        /// HTTP）。超时不重试（15s 已付出，重试价值低、拉长无反馈等待）；
        /// 鉴权/解析错误不重试（确定性失败）。</summary>
        internal static bool IsRetryable(string code)
        {
            return code == "network" || code == "rate_limited" || code == "server" || code == "http";
        }

        public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrEmpty(request.Text))
                throw new TranslateException("没有待翻译文本", "input");

            AppConfig cfg = _configProvider();
            string primaryId = ResolvePrimaryId(cfg);
            // 缓存键含【实际主 Provider】（ResolvePrimaryId 含门禁降级——
            // provider=llm 但未配置时主已落到 mymemory，键应与之一致）：
            // 切换服务商后同文本是新键，真实重翻
            //（2026-09-03 回归修复：原键不含 Provider，切服务商命中旧结果"没重翻"）
            string key = ResultCache.MakeKey(cfg.SourceLang, cfg.TargetLang, primaryId, request.Text);

            // ① 缓存优先（键含配置 Provider：降级结果存配置键下，TTL 内重复
            //    划词同样命中；用户切换 Provider 即新键，真实重翻）
            TranslationResult cached;
            if (_cache.TryGet(key, out cached))
            {
                Log("cache hit provider=" + cached.Provider
                    + " len=" + (cached.TranslatedText ?? "").Length);
                return new TranslationResult
                {
                    SourceText = cached.SourceText,
                    TranslatedText = cached.TranslatedText,
                    SourceLanguage = cached.SourceLanguage,
                    TargetLanguage = cached.TargetLanguage,
                    Provider = cached.Provider,
                    ElapsedMs = 0
                };
            }

            var sw = Stopwatch.StartNew();
            string fallbackId = ResolveFallbackId(cfg, primaryId);

            string primaryError = null, primaryCode = null;
            string fallbackError = null;
            bool fallbackAttempted = false;

            string[] chain = fallbackId != null ? new[] { primaryId, fallbackId } : new[] { primaryId };
            foreach (string id in chain)
            {
                ITranslationProvider provider = _providerFactory(cfg, id);
                if (provider == null) continue;   // 如百度密钥缺失
                if (id != primaryId) fallbackAttempted = true;

                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        string translated = await provider.TranslateAsync(
                            request.Text, cfg.SourceLang, cfg.TargetLang, ct);
                        sw.Stop();
                        var result = new TranslationResult
                        {
                            SourceText = request.Text,
                            TranslatedText = translated,
                            SourceLanguage = cfg.SourceLang,
                            TargetLanguage = cfg.TargetLang,
                            Provider = provider.Id,
                            ElapsedMs = sw.ElapsedMilliseconds
                        };
                        _cache.Put(key, result);
                        if (id != primaryId || attempt > 1)
                            Log("success via=" + provider.Id + " attempt=" + attempt
                                + " elapsed=" + result.ElapsedMs + "ms");
                        return result;
                    }
                    catch (TranslateException ex)
                    {
                        Log("fail provider=" + provider.Id + " attempt=" + attempt
                            + " code=" + ex.Code);
                        if (id == primaryId) { primaryError = ex.Message; primaryCode = ex.Code; }
                        else fallbackError = ex.Message;

                        if (attempt < MaxAttempts && IsRetryable(ex.Code))
                        {
                            int delay = RetryBackoffMs[Math.Min(attempt - 1, RetryBackoffMs.Length - 1)];
                            Log("backoff " + delay + "ms provider=" + provider.Id);
                            if (delay > 0) await Task.Delay(delay, ct);
                            continue;
                        }
                        break;   // 该 Provider 放弃 → 降级下一个
                    }
                }
            }

            // 全部尝试失败：以主 Provider 错误为主文案，备用结果附注
            if (primaryError != null)
            {
                string msg = primaryError;
                if (fallbackAttempted && fallbackError != null)
                    msg += "\n\n备用提供商也失败：" + fallbackError;
                throw new TranslateException(msg, primaryCode);
            }
            throw new TranslateException("没有可用的翻译提供商。", "config");
        }

        /// <summary>诊断日志（同 %TEMP%\tfd_host_err.log [core] 通道；只记
        /// code/len/provider 等元数据，不记文本内容）</summary>
        private static void Log(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tfd_host_err.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " [core] translate: " + msg + "\r\n");
            }
            catch (Exception) { }
        }
    }
}
