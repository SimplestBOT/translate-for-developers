//=============================================================
// Providers/TranslateException.cs - 业务翻译异常（带错误码）
// Message = 用户可直接展示的中文文案（经协议 error 帧
//   {code:"translate_failed", message} 到达页面，UI 不解析原始 JSON）；
// Code = 机器可读分类（timeout/network/rate_limited/auth/server/http/
//   parse/input），供 TranslationService 重试/降级决策与诊断日志——
//   错误帧 code 恒为 translate_failed（协议 v1.4 页面按此路由错误卡）。
// 工厂方法统一 HTTP/超时/连接三类异常的文案映射（两 Provider 共用）。
//=============================================================
using System;

namespace Translator.Core.Providers
{
    public sealed class TranslateException : Exception
    {
        /// <summary>机器可读分类（见文件头），用于重试/降级决策与日志</summary>
        public readonly string Code;

        public TranslateException(string message, string code)
            : base(message)
        {
            Code = code ?? "unknown";
        }

        /// <summary>兼容旧调用点（无分类）</summary>
        public TranslateException(string message) : this(message, "unknown") { }

        /// <summary>HTTP 非 200：429=限流，401/403=鉴权失败（确定性错误，
        /// 不重试直接降级），5xx=服务端错误，其余=一般 HTTP 错误</summary>
        public static TranslateException Http(int status, string providerName)
        {
            if (status == 429)
                return new TranslateException(providerName + " 限流（HTTP 429）：请求过于频繁，请稍后重试。", "rate_limited");
            if (status == 401 || status == 403)
                return new TranslateException(
                    providerName + " API Key 无效或无权限（HTTP " + status + "）。\n请检查 Key 是否正确、是否与所用端点匹配（免费版/Pro 端点不同）。", "auth");
            if (status >= 500)
                return new TranslateException(providerName + " 服务错误（HTTP " + status + "）：服务端暂时不可用。", "server");
            return new TranslateException(providerName + " 返回错误（HTTP " + status + "）。\n请检查网络后重试。", "http");
        }

        /// <summary>请求超时（15s，HttpClient 级）</summary>
        public static TranslateException Timeout(string providerName)
        {
            return new TranslateException(
                "网络请求超时（" + (Infrastructure.Http.ApiTimeoutMs / 1000) + " 秒）：" + providerName + " 无响应。\n请检查网络后重试。", "timeout");
        }

        /// <summary>连接类失败（DNS/断网/代理拒绝）</summary>
        public static TranslateException Network(string providerName, Exception inner)
        {
            return new TranslateException(
                "网络连接失败：无法连接 " + providerName + " 服务。\n请检查网络/代理后重试。", "network");
        }
    }
}
