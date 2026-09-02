//=============================================================
// Translation/TranslationService.cs - 翻译组合层（async 第一天）
// 硬约束（architecture.md 边界规则 5）：
//   Task<TranslationResult> TranslateAsync(TranslationRequest, CancellationToken)
//   取消源 = 窗口关闭 / 重试 / 宿主退出 / 管道断开（由宿主注入 CTS）
// UI 只消费统一 TranslationResult 模型，永不接触 Provider 原始 JSON。
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
        private readonly Func<AppConfig> _configProvider;

        /// <summary>生产构造：从 ConfigStore 快照取配置（写方=宿主，每次翻译取当下值）</summary>
        public TranslationService(ConfigStore store) : this(delegate { return store.Current; }) { }

        /// <summary>测试构造：任意配置源</summary>
        public TranslationService(Func<AppConfig> configProvider)
        {
            if (configProvider == null) throw new ArgumentNullException("configProvider");
            _configProvider = configProvider;
        }

        public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrEmpty(request.Text))
                throw new TranslateException("没有待翻译文本");

            AppConfig cfg = _configProvider();
            // 分发规则对齐 translator.ahk TranslateText：
            // provider=baidu 且密钥齐备 → 百度，否则一律 MyMemory
            ITranslationProvider provider =
                cfg.Provider == "baidu" && cfg.HasBaiduKeys()
                    ? (ITranslationProvider)new BaiduProvider(cfg.BaiduAppid, cfg.BaiduSecret)
                    : new MyMemoryProvider();

            var sw = Stopwatch.StartNew();
            string translated = await provider.TranslateAsync(request.Text, cfg.SourceLang, cfg.TargetLang, ct);
            sw.Stop();

            return new TranslationResult
            {
                SourceText = request.Text,
                TranslatedText = translated,
                SourceLanguage = cfg.SourceLang,
                TargetLanguage = cfg.TargetLang,
                Provider = provider.Id,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
    }
}
