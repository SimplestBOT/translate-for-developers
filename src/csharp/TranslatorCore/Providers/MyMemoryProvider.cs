//=============================================================
// Providers/MyMemoryProvider.cs - MyMemory 免费 API
// 对齐 translator.ahk TranslateMyMemory：
//   - 单次请求限 500 字符 → 按字符分片 450
//   - 源语言 auto 时用 "Autodetect"（MyMemory 有效写法）
//   - 响应取 responseData.translatedText；失败看 responseDetails
// 行为微调（相比 AHK 正则版）：responseStatus != 200 时把
//   responseDetails 作为错误抛出（AHK 正则会把配额耗尽的空
//   translatedText 当成功拼进结果，表现为「翻译出空串」）。
//=============================================================
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Translator.Core.Infrastructure;

namespace Translator.Core.Providers
{
    public sealed class MyMemoryProvider : ITranslationProvider
    {
        private const string Endpoint = "https://api.mymemory.translated.net/get?q=";

        public string Id { get { return "mymemory"; } }
        public string DisplayName { get { return "MyMemory"; } }

        public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
        {
            // 免费 API 单次限 500 字符，450 留余量（与 AHK 同值）
            List<string> parts = TextSplitter.SplitByChars(text, 450);
            string srcCode = sourceLang == "auto" ? "Autodetect" : sourceLang;
            var result = new StringBuilder();
            foreach (string part in parts)
            {
                ct.ThrowIfCancellationRequested(); // 分片循环每片检查取消（硬约束）
                string url = Endpoint
                    + Uri.EscapeDataString(part)
                    + "&langpair=" + Uri.EscapeDataString(srcCode + "|" + targetLang);
                string body;
                try
                {
                    body = await Http.GetStringAsync(url, ct);
                }
                catch (HttpStatusException ex)
                {
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

        /// <summary>解析单分片响应。异常 → TranslateException（带分类码）；
        /// internal 供测试。responseStatus 非 200 时优先按「额度/限流」识别
        /// （免费号当日配额耗尽返回 403 + MYMEMORY WARNING 文案），其余归
        /// server（服务端拒绝）。</summary>
        internal static string ParseResult(string body)
        {
            var root = JsonUtil.ParseObject(body);
            if (root != null)
            {
                var rd = root.ContainsKey("responseData")
                    ? root["responseData"] as Dictionary<string, object>
                    : null;
                string translated = rd != null ? JsonUtil.GetString(rd, "translatedText") : null;
                if (!string.IsNullOrEmpty(translated))
                    return translated;

                // 无有效译文：优先给出服务端明细（配额耗尽 / 语言对非法等）
                string details = JsonUtil.GetString(root, "responseDetails");
                if (!string.IsNullOrEmpty(details))
                    throw new TranslateException("MyMemory 翻译失败：" + details, ClassifyResponseStatus(root, details));

                if (translated != null) // translatedText 存在但为空且无明细：按成功处理（对齐 AHK）
                    return translated;
            }
            throw new TranslateException("MyMemory 翻译失败：无法解析服务响应。", "parse");
        }

        private static string ClassifyResponseStatus(Dictionary<string, object> root, string details)
        {
            int status = JsonUtil.GetInt(root, "responseStatus");
            string up = details.ToUpperInvariant();
            bool quota = up.Contains("LIMIT") || up.Contains("QUOTA")
                || up.Contains("FREE TRANSLATIONS") || up.Contains("MAXIMUM");
            if (status == 429 || status == 403 || quota)
                return "rate_limited";
            return "server";
        }
    }
}
