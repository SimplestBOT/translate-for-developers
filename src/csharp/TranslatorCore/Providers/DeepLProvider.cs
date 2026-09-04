//=============================================================
// Providers/DeepLProvider.cs - DeepL 官方 API（v2/translate）
// 端点：免费 key（:fx 结尾特征？官方文档未承诺——默认免费端点
//   api-free.deepl.com，Pro 用户经 config.deepl_endpoint 切
//   api.deepl.com；不猜测 key 形态）。鉴权：DeepL-Auth-Key 头。
// 请求：application/x-www-form-urlencoded，text 可重复（V1 单 text 分片）。
//   source_lang 省略 = 官方自动检测；target_lang 必填。
// 语言码：LanguageTable id → 大写（en→EN）；zh-CN→ZH、zh-TW→ZH-TW 特例；
//   其余 id 大写直传，是否支持由服务端裁决（错误经统一错误处理呈现，
//   不本地硬编码未验证的支持表）。
// 错误：沿用统一分类（429/456 配额→rate_limited，401/403→auth，
//   5xx→server）；解析失败→parse。
//=============================================================
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Translator.Core.Configuration;
using Translator.Core.Infrastructure;

namespace Translator.Core.Providers
{
    public sealed class DeepLProvider : ITranslationProvider
    {
        public const string DefaultEndpoint = "https://api-free.deepl.com/v2/translate";
        // 单片字符上限：保守值（官方上限远大于此且随计划变化，不硬编码断言）
        private const int ChunkChars = 5000;

        private readonly string _authKey;
        private readonly string _endpoint;

        public DeepLProvider(string authKey, string endpoint)
        {
            _authKey = authKey ?? "";
            _endpoint = NormalizeEndpoint(endpoint);
        }

        /// <summary>端点规范化（internal 供测试）：容错用户填法——
        /// 空默认；无 scheme 补 https://；裸主机（无路径）补 /v2/translate；
        /// 完整 URL 原样。</summary>
        internal static string NormalizeEndpoint(string raw)
        {
            var e = (raw ?? "").Trim().TrimEnd('/');
            if (e.Length == 0) return DefaultEndpoint;
            if (!e.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                e = "https://" + e;
            int afterScheme = e.IndexOf("://", StringComparison.Ordinal) + 3;
            if (!e.Substring(afterScheme).Contains("/"))
                e += "/v2/translate";
            return e;
        }

        public string Id { get { return "deepl"; } }
        public string DisplayName { get { return "DeepL"; } }

        public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
        {
            List<string> parts = TextSplitter.SplitByChars(text, ChunkChars);
            var result = new StringBuilder();
            foreach (string part in parts)
            {
                ct.ThrowIfCancellationRequested(); // 分片循环每片检查取消（硬约束）
                var form = new StringBuilder();
                form.Append("text=").Append(Uri.EscapeDataString(part));
                form.Append("&target_lang=").Append(Uri.EscapeDataString(ToDeepLCode(targetLang)));
                if (!string.IsNullOrEmpty(sourceLang) && sourceLang != "auto")
                    form.Append("&source_lang=").Append(Uri.EscapeDataString(ToDeepLCode(sourceLang)));

                string body;
                try
                {
                    // 官方鉴权格式：Authorization: DeepL-Auth-Key <key>
                    //（DeepL-Auth-Key 是 Authorization 头内的 scheme，不是独立头名；
                    //  误作独立头时服务器视为未鉴权 → 403 Missing Authorization）
                    var headers = new[]
                    {
                        new KeyValuePair<string, string>("Authorization", "DeepL-Auth-Key " + _authKey)
                    };
                    body = await Http.PostFormAsync(_endpoint, form.ToString(), headers, ct);
                }
                catch (HttpStatusException ex)
                {
                    if (ex.Status == 456)
                        throw new TranslateException("DeepL 配额已用尽（HTTP 456）：免费版每月 50 万字符，请下月再试或升级。",
                            "rate_limited");
                    throw TranslateException.Http(ex.Status, DisplayName);
                }
                catch (TimeoutException)
                {
                    throw TranslateException.Timeout(DisplayName);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    throw TranslateException.Network(DisplayName, ex);
                }
                result.Append(ParseResult(body));
            }
            return result.ToString();
        }

        /// <summary>解析单分片响应（translations[0].text）。internal 供测试。</summary>
        internal static string ParseResult(string body)
        {
            var root = JsonUtil.ParseObject(body);
            var list = root != null ? JsonUtil.GetList(root, "translations") : null;
            if (list != null && list.Count > 0)
            {
                var item = list[0] as Dictionary<string, object>;
                string t = item != null ? JsonUtil.GetString(item, "text") : null;
                if (!string.IsNullOrEmpty(t))
                    return t;
            }
            // DeepL 错误响应 {"message": "..."}——HTTP 层已分类，这里只兜解析
            throw new TranslateException("DeepL 翻译失败：无法解析服务响应。", "parse");
        }

        /// <summary>LanguageTable id → DeepL 语言码（internal 供测试）。</summary>
        internal static string ToDeepLCode(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "auto") return id;
            if (id == "zh-CN") return "ZH";
            if (id == "zh-TW") return "ZH-TW";   // 官方文档未列入 target 表：由服务端裁决
            if (id == "zh") return "ZH";
            return id.ToUpperInvariant();
        }
    }
}
