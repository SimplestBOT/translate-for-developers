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
                    throw new TranslateException("MyMemory 返回错误（HTTP " + ex.Status + "）。\n请检查网络后重试。");
                }
                catch (TimeoutException)
                {
                    throw new TranslateException("网络请求超时（15 秒）。\n请检查网络后重试。");
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    throw new TranslateException("网络请求失败：" + ex.Message);
                }
                result.Append(ParseResult(body));
            }
            return result.ToString();
        }

        /// <summary>解析单分片响应。异常 → TranslateException；internal 供测试。</summary>
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
                    throw new TranslateException("MyMemory 翻译失败：" + details);

                if (translated != null) // translatedText 存在但为空且无明细：按成功处理（对齐 AHK）
                    return translated;
            }
            throw new TranslateException("MyMemory 翻译失败：无法解析服务响应。");
        }
    }
}
