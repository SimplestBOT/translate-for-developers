//=============================================================
// Providers/OpenAICompatibleProvider.cs - OpenAI-compatible LLM 翻译（唯一实现）
// 覆盖 OpenAI / DeepSeek / Kimi / 智谱 / Ollama / 任意自定义——差异全部在
//   配置（ProviderCatalog 预设模板 + config 字段），不写多份 Provider。
// 请求：POST {base}/chat/completions，JSON {model, messages, temperature:0.1}
//   messages = [system(prompt+语言指令), user(保护后文本)]；
//   ApiKey 为空（Ollama 本地）→ 不带 Authorization 头。
// 响应：choices[0].message.content。错误：401/403→auth，400→bad_request
//   （模型名/请求体问题，不重试），404→server，429→rate_limited，5xx→server；
//   响应体 error.message 进诊断日志（不含 key/正文）。
// 开发者内容保护：发送前 DevTextGuard.Protect，译后 Restore——恢复失败抛
//   TranslateException（不静默），走现有重试/降级。
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
    public sealed class OpenAICompatibleProvider : ITranslationProvider
    {
        // 单片字符上限：保守值（各模型上下文差异大，V1 不做按模型探测）
        private const int ChunkChars = 4000;

        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _prompt;

        public OpenAICompatibleProvider(string baseUrl, string apiKey, string model, string prompt)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _apiKey = apiKey ?? "";
            _model = model ?? "";
            _prompt = string.IsNullOrEmpty(prompt) ? ProviderCatalog.DefaultLlmPrompt : prompt;
        }

        public string Id { get { return "llm"; } }
        public string DisplayName { get { return "AI 大模型"; } }

        public Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
        {
            List<string> parts = TextSplitter.SplitByChars(text, ChunkChars);
            var result = new StringBuilder();
            foreach (string part in parts)
            {
                ct.ThrowIfCancellationRequested(); // 分片循环每片检查取消（硬约束）
                result.Append(TranslateChunk(part, sourceLang, targetLang, ct));
            }
            return Task.FromResult(result.ToString());
        }

        private string TranslateChunk(string part, string sourceLang, string targetLang, CancellationToken ct)
        {
            // ① 开发者内容保护（仅 LLM 路径需要；原文含占位符字样时 Protect 原样返回）
            DevTextGuard.Protected guarded = DevTextGuard.Protect(part);

            // ② 组装请求体
            var sys = new StringBuilder(_prompt);
            sys.Append("\n\n目标语言：").Append(LanguageTable.DisplayName(targetLang));
            if (!string.IsNullOrEmpty(sourceLang) && sourceLang != "auto")
                sys.Append("\n源语言：").Append(LanguageTable.DisplayName(sourceLang));
            else
                sys.Append("\n源语言：自动检测");

            var messages = new List<object>
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", sys.ToString() } },
                new Dictionary<string, object> { { "role", "user" }, { "content", guarded.Text } }
            };
            string json = JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "model", _model },
                { "messages", messages },
                { "temperature", 0.1 }
            });

            string body;
            try
            {
                var headers = new List<KeyValuePair<string, string>>();
                if (_apiKey.Length > 0)
                    headers.Add(new KeyValuePair<string, string>("Authorization", "Bearer " + _apiKey));
                body = Http.PostJsonAsync(_baseUrl + "/chat/completions", json, headers, ct)
                    .GetAwaiter().GetResult();
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

            // ③ 解析
            string content = ParseResult(body);
            if (string.IsNullOrEmpty(content))
                throw new TranslateException("AI 大模型返回空译文。", "parse");

            // ④ 恢复占位符：缺失/被改写 → 明确失败（不静默返回坏译文）
            string restored = DevTextGuard.Restore(content, guarded.Tokens);
            if (restored == null)
                throw new TranslateException(
                    "AI 大模型改写或丢失了受保护内容标记（__TFD_G*__），译文不可用。\n请重试，或更换模型/服务商。",
                    "parse");
            return restored;
        }

        /// <summary>解析 chat/completions 响应取 choices[0].message.content。
        /// internal 供测试。</summary>
        internal static string ParseResult(string body)
        {
            var root = JsonUtil.ParseObject(body);
            var choices = root != null ? JsonUtil.GetList(root, "choices") : null;
            if (choices != null && choices.Count > 0)
            {
                var choice = choices[0] as Dictionary<string, object>;
                var msg = choice != null ? choice["message"] as Dictionary<string, object> : null;
                string content = msg != null ? JsonUtil.GetString(msg, "content") : null;
                if (content != null)
                    return content.Trim();
            }
            throw new TranslateException("AI 大模型翻译失败：无法解析服务响应。", "parse");
        }

        /// <summary>构造请求体 JSON（internal 供测试：校验 model/messages/温度）。</summary>
        internal static string BuildRequestJson(string model, string systemPrompt, string userText)
        {
            var messages = new List<object>
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } },
                new Dictionary<string, object> { { "role", "user" }, { "content", userText } }
            };
            return JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "model", model },
                { "messages", messages },
                { "temperature", 0.1 }
            });
        }
    }
}
