//=============================================================
// Infrastructure/Http.cs - HttpClient 单例 + GET 帮助器
// 超时联动：gApiTimeout=15000ms（translator.ahk 同值），应用于
// HttpClient.Timeout（整请求级）；取消经 CancellationToken 传入，
// 分片循环每片由 Provider 检查 ct（硬约束：分片循环每片检查取消）。
//=============================================================
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Translator.Core.Infrastructure
{
    /// <summary>HTTP 状态码非 200 时抛出（Provider 映射为友好错误文案）</summary>
    public sealed class HttpStatusException : Exception
    {
        public readonly int Status;
        public HttpStatusException(int status) : base("HTTP " + status) { Status = status; }
    }

    public static class Http
    {
        /// <summary>API 超时（毫秒），与 translator.ahk gApiTimeout 保持一致</summary>
        public const int ApiTimeoutMs = 15000;

        private static readonly Lazy<HttpClient> _client = new Lazy<HttpClient>(delegate
        {
            // net48 默认协议随系统（Win10+ 含 TLS1.2），显式补充以防旧系统策略
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromMilliseconds(ApiTimeoutMs);
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) translator-for-developers");
            return c;
        });

        private static HttpClient Client { get { return _client.Value; } }

        /// <summary>
        /// GET 并返回响应体。约定异常映射：
        ///   非 200 → HttpStatusException；超时 → TimeoutException；
        ///   连接类失败 → HttpRequestException 原样上抛；
        ///   外部取消 → OperationCanceledException 原样上抛（Provider 不得吞掉）。
        /// 诊断日志（请求级，不含 URL 查询串内容）写 %TEMP%\tfd_host_err.log。
        /// </summary>
        public static async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            string tag = url.Split('/')[2];
            Log("GET https://" + tag + " ...");
            HttpResponseMessage resp;
            try
            {
                resp = await Client.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            }
            catch (OperationCanceledException)
            {
                // HttpClient.Timeout 触发的取消不经过外部 ct：此时外部未取消 → 视为超时
                if (ct.IsCancellationRequested) throw;
                Log("GET " + tag + " -> timeout");
                throw new TimeoutException("网络请求超时（" + (ApiTimeoutMs / 1000) + " 秒）");
            }
            using (resp)
            {
                Log("GET " + tag + " -> HTTP " + (int)resp.StatusCode);
                if ((int)resp.StatusCode != 200)
                    throw new HttpStatusException((int)resp.StatusCode);
                return await resp.Content.ReadAsStringAsync();
            }
        }

        private static void Log(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tfd_host_err.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " [core] " + msg + "\r\n");
            }
            catch (Exception) { }
        }
    }
}
