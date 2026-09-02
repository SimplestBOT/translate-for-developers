//=============================================================
// Providers/BaiduProvider.cs - 百度翻译开放平台（免费标准版）
// 对齐 translator.ahk TranslateBaidu + Md5Hex + BaiduLang：
//   - 单次 q 限 6000 字节 → 按 UTF-8 字节分片 5000
//   - 签名 MD5(appid + q + salt + secret) 小写 hex（q 为原文，URL 里才编码）
//   - 多行 q 返回多条 trans_result，dst 按 \n 拼接（与 AHK 循环取 dst 一致）
// MD5 用 BCL（AHK 因无内置才手写实现，输出同为小写 hex，值恒等）
//=============================================================
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Translator.Core.Configuration;
using Translator.Core.Infrastructure;

namespace Translator.Core.Providers
{
    public sealed class BaiduProvider : ITranslationProvider
    {
        private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate?q=";
        private readonly string _appid;
        private readonly string _secret;
        private readonly Random _saltRnd;

        public BaiduProvider(string appid, string secret)
        {
            _appid = appid ?? "";
            _secret = secret ?? "";
            _saltRnd = new Random(); // 实例每请求新建，实例内 Random 非线程安全但仅单线程使用
        }

        public string Id { get { return "baidu"; } }
        public string DisplayName { get { return "百度翻译"; } }

        public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
        {
            List<string> parts = TextSplitter.SplitByBytes(text, 5000);
            string fromCode = LanguageTable.ToBaiduCode(sourceLang); // auto → "auto"（百度原生支持）
            string toCode = LanguageTable.ToBaiduCode(targetLang);
            var result = new StringBuilder();
            foreach (string part in parts)
            {
                ct.ThrowIfCancellationRequested(); // 分片循环每片检查取消（硬约束）
                int salt = _saltRnd.Next(100000, 1000000); // AHK Random(100000, 999999) 同区间
                string sign = Md5Hex(_appid + part + salt + _secret);
                string url = Endpoint
                    + Uri.EscapeDataString(part)
                    + "&from=" + Uri.EscapeDataString(fromCode)
                    + "&to=" + Uri.EscapeDataString(toCode)
                    + "&appid=" + Uri.EscapeDataString(_appid)
                    + "&salt=" + salt
                    + "&sign=" + sign;
                string body;
                try
                {
                    body = await Http.GetStringAsync(url, ct);
                }
                catch (HttpStatusException ex)
                {
                    throw new TranslateException("百度翻译返回错误（HTTP " + ex.Status + "）。\n请检查网络后重试。");
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

        /// <summary>解析单分片响应（多条 dst 以 \n 拼接）。internal 供测试。</summary>
        internal static string ParseResult(string body)
        {
            var root = JsonUtil.ParseObject(body);
            if (root == null)
                throw new TranslateException("百度翻译失败：无法解析响应。");

            string errCode = JsonUtil.GetString(root, "error_code");
            if (errCode != null)
            {
                string errMsg = JsonUtil.GetString(root, "error_msg") ?? "";
                throw new TranslateException("百度翻译错误 " + errCode + "：" + errMsg
                    + "\n\n如为 52003/54001，请检查 APP ID 和密钥是否正确；\n如为 54003，请确认已开通该翻译服务。");
            }

            var list = JsonUtil.GetList(root, "trans_result");
            if (list != null)
            {
                var dsts = new List<string>();
                foreach (var item in list)
                {
                    var d = item as Dictionary<string, object>;
                    string dst = d != null ? JsonUtil.GetString(d, "dst") : null;
                    if (dst != null)
                        dsts.Add(dst);
                }
                if (dsts.Count > 0)
                    return string.Join("\n", dsts);
            }
            throw new TranslateException("百度翻译失败：无法解析响应。");
        }

        /// <summary>UTF-8 MD5 小写 hex（等价 AHK Md5Hex，测试含 RFC1321 标准向量）</summary>
        internal static string Md5Hex(string s)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
