//=============================================================
// Program.cs (Tests) - 业务核心自测入口
// 用例来源：docs/protocol.md §4 统一模型 + translator.ahk 行为对齐
// 运行：dotnet run --project csharp/TranslatorCore.Tests -c Release
//=============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Translator.Core.Configuration;
using Translator.Core.Infrastructure;
using Translator.Core.Providers;
using Translator.Core.Translation;

namespace Translator.Core.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            SplitterTests.Register();
            Md5Tests.Register();
            ConfigTests.Register();
            ProviderParseTests.Register();
            JsonUtilTests.Register();
            ServiceTests.Register();
            TerminalGuardTests.Register();
            UiaTextTests.Register();
            ResilienceTests.Register();
            SecretProtectionTests.Register();
            NewProviderTests.Register();

            Console.WriteLine("SELFTEST-CORE " + TestRunner.Summary);
            return TestRunner.ExitCode;
        }

        private static void Section(string name, Action body) { TestRunner.Section(name, body); }
        private static void Eq(object e, object a, string w) { TestRunner.Eq(e, a, w); }
        private static void True(bool c, string w) { TestRunner.True(c, w); }
        private static void False(bool c, string w) { TestRunner.False(c, w); }
        private static void SeqEq(List<string> expect, List<string> actual, string what)
        {
            bool same = expect != null && actual != null && expect.Count == actual.Count;
            if (same)
                for (int i = 0; i < expect.Count; i++)
                    if (!Equals(expect[i], actual[i])) { same = false; break; }
            TestRunner.True(same, what + " (expect=[" + string.Join("|", expect) + "] actual=[" + string.Join("|", actual) + "])");
        }

        private static class SplitterTests
        {
            public static void Register()
            {
                Section("SplitByChars", delegate
                {
                    // 短文本：单分片
                    SeqEq(List("hello"), TextSplitter.SplitByChars("hello", 450), "短文本单分片");

                    // 多行不超限：保留 \n 于片内
                    SeqEq(List("a\nb\nc"), TextSplitter.SplitByChars("a\nb\nc", 450), "多行合片");

                    // 行累计超限：超限块整块推出（AHK 算法原样：10+\n+10=21 > 12 → 整块为一片）
                    var p1 = TextSplitter.SplitByChars("aaaaaaaaaa\nbbbbbbbbbb\ncccccccccc", 12);
                    SeqEq(List("aaaaaaaaaa\nbbbbbbbbbb", "cccccccccc"), p1, "行累计超限整块推出");

                    // 超长单行：行内硬切，无分隔
                    var p2 = TextSplitter.SplitByChars("0123456789ABCDEF", 10);
                    SeqEq(List("0123456789", "ABCDEF"), p2, "超长单行硬切");

                    // 空文本：0 分片（与 AHK StrSplit 行为一致）
                    Eq(0, TextSplitter.SplitByChars("", 10).Count, "空文本");
                });

                Section("SplitByBytes", delegate
                {
                    // ASCII：1 字节/字符
                    var p1 = TextSplitter.SplitByBytes("aaaaaaaaaaaaaaaaaaaa", 10);
                    SeqEq(List("aaaaaaaaaa", "aaaaaaaaaa"), p1, "ASCII 切分");

                    // CJK：3 字节/字 —— "中文字测试" 共 5 字 15 字节，限 10 → 3+3+3 | 3+3
                    SeqEq(List("中文字", "测试"), TextSplitter.SplitByBytes("中文字测试", 10), "CJK 字节切分不劈字");

                    // 行合并 + 字节累计：abc(3)+\n(1)+def(3)=7 ≤10；ghi 再加 4 → 11 >10 切
                    SeqEq(List("abc\ndef", "ghi"), TextSplitter.SplitByBytes("abc\ndef\nghi", 10), "行边界按字节切");

                    // 空文本
                    Eq(0, TextSplitter.SplitByBytes("", 10).Count, "空文本");
                });
            }
        }

        private static List<string> List(params string[] items) { return new List<string>(items); }
        private static List<string> List(List<string> src) { return src; }

        private static class Md5Tests
        {
            public static void Register()
            {
                Section("Md5Hex", delegate
                {
                    // RFC1321 标准向量
                    Eq("d41d8cd98f00b204e9800998ecf8427e", BaiduProvider.Md5Hex(""), "空串");
                    Eq("900150983cd24fb0d6963f7d28e17f72", BaiduProvider.Md5Hex("abc"), "abc");
                    Eq("9e107d9d372bb6826bd81d3542a419d6",
                        BaiduProvider.Md5Hex("The quick brown fox jumps over the lazy dog"), "fox");
                    // UTF-8 多字节（百度签名对中文 q 的关键路径）
                    Eq("a7bac2239fcdcb3a067903d8077c4a07", BaiduProvider.Md5Hex("中文"), "中文 UTF-8 字节");
                });
            }
        }

        private static class ConfigTests
        {
            public static void Register()
            {
                Section("ConfigStore round-trip", delegate
                {
                    string dir = Path.Combine(Path.GetTempPath(), "tfd-cfgtest-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "config.conf");
                    try
                    {
                        var cfg = new AppConfig
                        {
                            Hotkey = "^!d",
                            Provider = "baidu",
                            SourceLang = "en",
                            TargetLang = "ja",
                            BaiduAppid = "20240101@test",
                            BaiduSecret = "s=ecret\"quote"
                        };
                        var store = new ConfigStore(path);
                        True(store.WriteSync(cfg), "首次写入成功");

                        // AHK LoadConfig 兼容读（逐行 key=value）
                        // 密钥行断言（优化 3）：磁盘不再有明文密钥——行内容为
                        // dpapi: 密文，特殊字符含在密文内不受转义影响
                        string content = File.ReadAllText(path, Encoding.UTF8);
                        True(content.Contains("hotkey=^!d"), "含 hotkey");
                        False(content.Contains("s=ecret\"quote"), "磁盘无明文 secret（DPAPI 加密）");
                        True(content.Contains("baidu_secret=" + SecretProtector.Prefix), "secret 行为 dpapi 密文");
                        True(File.ReadAllBytes(path)[0] == 0xEF, "带 BOM（对齐 AHK FileOpen UTF-8）");

                        var store2 = new ConfigStore(path);
                        var back = store2.Current;
                        Eq("^!d", back.Hotkey, "回读 hotkey");
                        Eq("baidu", back.Provider, "回读 provider");
                        Eq("en", back.SourceLang, "回读 src");
                        Eq("ja", back.TargetLang, "回读 tgt");
                        Eq("20240101@test", back.BaiduAppid, "回读 appid");
                        Eq("s=ecret\"quote", back.BaiduSecret, "回读 secret");
                    }
                    finally
                    {
                        try { Directory.Delete(dir, true); } catch (IOException) { }
                    }
                });

                Section("ConfigStore defaults+comments", delegate
                {
                    string dir = Path.Combine(Path.GetTempPath(), "tfd-cfgtest-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "config.conf");
                    try
                    {
                        File.WriteAllText(path,
                            "; comment line\n" +
                            "\n" +
                            "hotkey=^!x\n" +
                            "provider=google\n" +          // 非法值：忽略，走默认
                            "src_lang=zh-CN\n" +
                            "tgt_lang=auto\n" +            // 非法（目标不能 auto）：忽略
                            "bogus=yes\n",                 // 未知键：忽略
                            new UTF8Encoding(false));
                        var store = new ConfigStore(path);
                        Eq("^!x", store.Current.Hotkey, "合法 hotkey 生效");
                        Eq("mymemory", store.Current.Provider, "非法 provider 回默认");
                        Eq("zh-CN", store.Current.SourceLang, "合法 src 生效");
                        Eq("zh-CN", store.Current.TargetLang, "tgt=auto 回默认");
                    }
                    finally
                    {
                        try { Directory.Delete(dir, true); } catch (IOException) { }
                    }
                });

                Section("ConfigStore missing file", delegate
                {
                    string path = Path.Combine(Path.GetTempPath(), "tfd-cfgtest-none-" + Guid.NewGuid().ToString("N"), "config.conf");
                    var store = new ConfigStore(path);
                    Eq("^!t", store.Current.Hotkey, "缺文件走默认热键");
                    Eq("mymemory", store.Current.Provider, "缺文件走默认 provider");
                });
            }
        }

        private static class ProviderParseTests
        {
            public static void Register()
            {
                Section("MyMemory parse", delegate
                {
                    Eq("你好世界",
                        MyMemoryProvider.ParseResult("{\"responseData\":{\"translatedText\":\"\\u4f60\\u597d\\u4e16\\u754c\"},\"responseDetails\":\"\",\"responseStatus\":200}"),
                        "\\uXXXX 解码");
                    Eq("hi",
                        MyMemoryProvider.ParseResult("{\"responseData\":{\"translatedText\":\"hi\"},\"responseStatus\":200}"),
                        "普通文本");
                    TestRunner.Throws<TranslateException>(delegate
                    {
                        MyMemoryProvider.ParseResult("{\"responseData\":{\"translatedText\":\"\"},\"responseDetails\":\"MYMEMORY WARNING: QUOTA\",\"responseStatus\":403}");
                    }, "配额耗尽抛明细");
                    TestRunner.Throws<TranslateException>(delegate
                    {
                        MyMemoryProvider.ParseResult("not json");
                    }, "坏 JSON 抛无法解析");
                    Eq("", MyMemoryProvider.ParseResult("{\"responseData\":{\"translatedText\":\"\"},\"responseDetails\":\"\",\"responseStatus\":200}"),
                        "空译文+无明细=成功空串（对齐 AHK）");
                });

                Section("Baidu parse", delegate
                {
                    Eq("你好\n世界",
                        BaiduProvider.ParseResult("{\"from\":\"en\",\"to\":\"zh\",\"trans_result\":[{\"src\":\"hello\",\"dst\":\"\\u4f60\\u597d\"},{\"src\":\"world\",\"dst\":\"\\u4e16\\u754c\"}]}"),
                        "多 dst 按 \\n 拼接");
                    TestRunner.Throws<TranslateException>(delegate
                    {
                        BaiduProvider.ParseResult("{\"error_code\":\"52003\",\"error_msg\":\"UNAUTHORIZED USER\"}");
                    }, "error_code 抛含提示文案");
                    TestRunner.Throws<TranslateException>(delegate
                    {
                        BaiduProvider.ParseResult("{\"error_code\":\"54003\",\"error_msg\":\"ACCESS_LIMIT\"}");
                    }, "54003 同样抛出");
                    TestRunner.Throws<TranslateException>(delegate
                    {
                        BaiduProvider.ParseResult("{}");
                    }, "无 trans_result 抛无法解析");
                });

                Section("LanguageTable", delegate
                {
                    Eq("zh", LanguageTable.ToBaiduCode("zh-CN"), "zh-CN→zh");
                    Eq("cht", LanguageTable.ToBaiduCode("zh-TW"), "zh-TW→cht");
                    Eq("jp", LanguageTable.ToBaiduCode("ja"), "ja→jp");
                    Eq("iw", LanguageTable.ToBaiduCode("he"), "he→iw");
                    Eq("auto", LanguageTable.ToBaiduCode("auto"), "auto→auto");
                    Eq("xx", LanguageTable.ToBaiduCode("xx"), "未知原样透传");
                    True(LanguageTable.IsKnown("fil"), "fil 已知");
                    False(LanguageTable.IsKnown("xx"), "xx 未知");
                    False(LanguageTable.IsKnown(null), "null 非法");
                });
            }
        }

        private static class JsonUtilTests
        {
            public static void Register()
            {
                Section("JsonUtil", delegate
                {
                    var o = JsonUtil.ParseObject("{\"a\":\"x\\\"y\",\"n\":5,\"b\":true,\"arr\":[\"p\",\"q\"]}");
                    True(o != null, "解析成功");
                    Eq("x\"y", JsonUtil.GetString(o, "a"), "转义还原");
                    Eq(5, JsonUtil.GetInt(o, "n"), "整数");
                    True(JsonUtil.GetBool(o, "b"), "布尔");
                    var arr = JsonUtil.GetList(o, "arr");
                    True(arr != null && arr.Count == 2, "数组");
                    True(JsonUtil.ParseObject("broken") == null, "坏输入返回 null");
                    Eq("{\"k\":\"v\\\"w\"}", JsonUtil.Serialize(new Dictionary<string, object> { { "k", "v\"w" } }),
                        "序列化转义引号");
                });
            }
        }

        private static class ServiceTests
        {
            public static void Register()
            {
                Section("TranslationService dispatch", delegate
                {
                    // provider=baidu 但密钥缺失 → 落 MyMemory（对齐 TranslateText 规则）
                    var svc = new TranslationService(delegate
                    {
                        return new AppConfig { Provider = "baidu", SourceLang = "auto", TargetLang = "zh-CN", BaiduAppid = "", BaiduSecret = "" };
                    });
                    True(svc != null, "构造（离线不触发网络）");
                    // provider 选择是 private；此处仅验证 config 缺失不炸
                });

                Section("TranslationResult shape", delegate
                {
                    var r = new TranslationResult
                    {
                        SourceText = "hi",
                        TranslatedText = "你好",
                        SourceLanguage = "auto",
                        TargetLanguage = "zh-CN",
                        Provider = "mymemory",
                        ElapsedMs = 812
                    };
                    string json = JsonUtil.Serialize(new Dictionary<string, object>
                    {
                        { "sourceText", r.SourceText },
                        { "translatedText", r.TranslatedText },
                        { "sourceLanguage", r.SourceLanguage },
                        { "targetLanguage", r.TargetLanguage },
                        { "provider", r.Provider },
                        { "elapsedMs", r.ElapsedMs }
                    });
                    // JavaScriptSerializer 非 ASCII 原样输出（合法 JSON，管道/页面均 UTF-8；
                    // 与 AHK Q() 行为一致：不转义非 ASCII，只转义引号与控制字符）
                    Eq("{\"sourceText\":\"hi\",\"translatedText\":\"你好\",\"sourceLanguage\":\"auto\",\"targetLanguage\":\"zh-CN\",\"provider\":\"mymemory\",\"elapsedMs\":812}",
                        json, "协议 §4 统一模型键序");
                });
            }
        }

        private static class TerminalGuardTests
        {
            public static void Register()
            {
                Section("TerminalGuard.IsTerminal", delegate
                {
                    string why;
                    // 类名命中（conhost 全家 / Windows Terminal / PuTTY）
                    True(TerminalGuard.IsTerminal("ConsoleWindowClass", "cmd", out why)
                        && why == "class:ConsoleWindowClass", "conhost 类名命中");
                    True(TerminalGuard.IsTerminal("CASCADIA_HOSTING_WINDOW_CLASS", "WindowsTerminal", out why)
                        && why == "class:CASCADIA_HOSTING_WINDOW_CLASS", "Windows Terminal 类名命中");
                    True(TerminalGuard.IsTerminal("PuTTY", "notepad", out why)
                        && why == "class:PuTTY", "PuTTY 类名命中");
                    // 进程名精确命中（忽略大小写）
                    True(TerminalGuard.IsTerminal("Chrome_WidgetWin_1", "Code", out why)
                        && why == "proc:Code", "VS Code 进程名命中");
                    True(TerminalGuard.IsTerminal("notepad_class", "POWERSHELL", out why)
                        && why == "proc:POWERSHELL", "进程名忽略大小写");
                    // 进程名前缀命中（带后缀变体）
                    True(TerminalGuard.IsTerminal("", "Code - Insiders", out why)
                        && why == "proc:Code - Insiders", "Code - Insiders 前缀命中");
                    True(TerminalGuard.IsTerminal(null, "idea64", out why), "JetBrains 前缀命中");
                    // 不误判：浏览器/普通应用走原 Ctrl+C 路径
                    False(TerminalGuard.IsTerminal("Chrome_WidgetWin_1", "chrome", out why), "浏览器不误判");
                    False(TerminalGuard.IsTerminal("Notepad", "notepad", out why), "记事本不误判");
                    // 空输入安全（前台窗口缺失/进程查询失败场景）
                    False(TerminalGuard.IsTerminal(null, null, out why), "空输入安全");
                    False(TerminalGuard.IsTerminal("", "", out why), "空串安全");
                });
            }
        }

        private static class UiaTextTests
        {
            public static void Register()
            {
                Section("UiaText.Normalize", delegate
                {
                    // 无效候选（UIA 无选区/空 range/provider 返回空白）
                    Eq(null, UiaText.Normalize(null), "null 无效");
                    Eq(null, UiaText.Normalize(""), "空串无效");
                    Eq(null, UiaText.Normalize("   \r\n\t "), "纯空白无效");
                    // 有效候选：原样返回（不 Trim，与剪贴板路径行为一致）
                    Eq("hello", UiaText.Normalize("hello"), "普通文本");
                    Eq(" 中文 selected ", UiaText.Normalize(" 中文 selected "), "首尾空白保留");
                    // 截断防 provider 缺陷返回全文档
                    string big = new string('a', UiaText.MaxChars + 100);
                    Eq(UiaText.MaxChars, UiaText.Normalize(big).Length, "超长截断到 MaxChars");
                    Eq(UiaText.MaxChars, UiaText.Normalize(new string('中', UiaText.MaxChars)).Length, "CJK 截断按字符数");
                    // 边界：恰好等于上限不截断
                    Eq(UiaText.MaxChars, UiaText.Normalize(new string('b', UiaText.MaxChars)).Length, "等于上限不截断");
                });
            }
        }

        // 优化 2：Provider 降级 + 重试 + 缓存（离线假 Provider，不碰网络）
        private static class ResilienceTests
        {
            /// <summary>假 Provider：按脚本逐次返回译文或抛错，计数尝试次数</summary>
            private sealed class FakeProvider : ITranslationProvider
            {
                private readonly string _id;
                private readonly object[] _script;   // string=译文 / TranslateException=抛出
                public int Attempts;
                public FakeProvider(string id, params object[] script) { _id = id; _script = script; }
                public string Id { get { return _id; } }
                public string DisplayName { get { return _id; } }
                public System.Threading.Tasks.Task<string> TranslateAsync(
                    string text, string sourceLang, string targetLang, System.Threading.CancellationToken ct)
                {
                    int i = Attempts;
                    Attempts++;
                    if (i < _script.Length)
                    {
                        var tex = _script[i] as TranslateException;
                        if (tex != null) throw tex;
                        return System.Threading.Tasks.Task.FromResult((string)_script[i]);
                    }
                    return System.Threading.Tasks.Task.FromResult("eof");
                }
            }

            private static TranslationService MakeService(AppConfig cfg, params ITranslationProvider[] byId)
            {
                return new TranslationService(delegate { return cfg; }, delegate(AppConfig c, string id)
                {
                    foreach (var p in byId) if (p.Id == id) return p;
                    return null;
                }, new ResultCache(TimeSpan.FromMinutes(5)));
            }

            private static AppConfig Cfg(string provider, bool keys)
            {
                return new AppConfig
                {
                    Provider = provider, SourceLang = "en", TargetLang = "zh-CN",
                    BaiduAppid = keys ? "appid" : "", BaiduSecret = keys ? "secret" : ""
                };
            }

            private static TranslationResult Run(TranslationService svc, string text)
            {
                return svc.TranslateAsync(new TranslationRequest { Text = text },
                    System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            }

            public static void Register()
            {
                var oldBackoff = new[] { TranslationService.RetryBackoffMs[0], TranslationService.RetryBackoffMs[1] };
                TranslationService.RetryBackoffMs[0] = 0;   // 测试不真等退避（readonly 数组改元素）
                TranslationService.RetryBackoffMs[1] = 0;
                try
                {
                    Section("ResultCache", delegate
                    {
                        var now = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
                        DateTime cur = now;
                        var cache = new ResultCache(TimeSpan.FromMinutes(5), 2, delegate { return cur; });
                        var r = new TranslationResult { SourceText = "a", TranslatedText = "甲", Provider = "mymemory" };
                        cache.Put(ResultCache.MakeKey("en", "zh-CN", "mymemory", "a"), r);
                        TranslationResult got;
                        True(cache.TryGet(ResultCache.MakeKey("en", "zh-CN", "mymemory", "a"), out got), "命中");
                        Eq("甲", got.TranslatedText, "取回译文");

                        // 键隔离：语言对不同 / Provider 不同不串
                        False(cache.TryGet(ResultCache.MakeKey("ja", "zh-CN", "mymemory", "a"), out got), "语言对隔离");
                        False(cache.TryGet(ResultCache.MakeKey("en", "zh-CN", "baidu", "a"), out got), "Provider 隔离");

                        // TTL 过期（5min+1s）
                        cur = now.AddMinutes(5).AddSeconds(1);
                        False(cache.TryGet(ResultCache.MakeKey("en", "zh-CN", "mymemory", "a"), out got), "TTL 过期失效");

                        // LRU 淘汰：容量 2，a 用后变最新，插入 c 淘汰 b
                        cur = now;
                        var c2 = new ResultCache(TimeSpan.FromMinutes(5), 2, delegate { return cur; });
                        var ra = new TranslationResult { SourceText = "a", TranslatedText = "A", Provider = "mymemory" };
                        var rb = new TranslationResult { SourceText = "b", TranslatedText = "B", Provider = "mymemory" };
                        var rc = new TranslationResult { SourceText = "c", TranslatedText = "C", Provider = "mymemory" };
                        c2.Put(ResultCache.MakeKey("en", "zh-CN", "mymemory", "a"), ra);
                        c2.Put(ResultCache.MakeKey("en", "zh-CN", "mymemory", "b"), rb);
                        cur = now.AddSeconds(1);
                        True(c2.TryGet(ResultCache.MakeKey("en", "zh-CN", "mymemory", "a"), out got), "a 触摸变新");
                        c2.Put(ResultCache.MakeKey("en", "zh-CN", "mymemory", "c"), rc);   // 应淘汰 b
                        True(c2.TryGet(ResultCache.MakeKey("en", "zh-CN", "mymemory", "a"), out got), "a 存活");
                        False(c2.TryGet(ResultCache.MakeKey("en", "zh-CN", "mymemory", "b"), out got), "b 被 LRU 淘汰");
                        True(c2.TryGet(ResultCache.MakeKey("en", "zh-CN", "mymemory", "c"), out got), "c 存活");
                    });

                    Section("ErrorCodes", delegate
                    {
                        Eq("rate_limited", TranslateException.Http(429, "X").Code, "HTTP 429 → rate_limited");
                        Eq("auth", TranslateException.Http(401, "X").Code, "HTTP 401 → auth");
                        Eq("auth", TranslateException.Http(403, "X").Code, "HTTP 403 → auth");
                        Eq("server", TranslateException.Http(503, "X").Code, "HTTP 5xx → server");
                        Eq("http", TranslateException.Http(400, "X").Code, "HTTP 4xx → http");
                        False(TranslationService.IsRetryable("auth"), "auth 不重试（确定性失败直接降级）");
                        Eq("timeout", TranslateException.Timeout("X").Code, "超时 → timeout");
                        Eq("network", TranslateException.Network("X", null).Code, "连接失败 → network");
                        // Provider 解析层分类
                        try { MyMemoryProvider.ParseResult("{\"responseData\":{\"translatedText\":\"\"},\"responseStatus\":403,\"responseDetails\":\"MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY\"}"); Eq(false, true, "配额应抛"); }
                        catch (TranslateException ex) { Eq("rate_limited", ex.Code, "MyMemory 配额耗尽 → rate_limited"); }
                        try { BaiduProvider.ParseResult("{\"error_code\":\"52003\",\"error_msg\":\"invalid api\"}"); Eq(false, true, "鉴权应抛"); }
                        catch (TranslateException ex) { Eq("auth", ex.Code, "百度 52003 → auth"); }
                        try { BaiduProvider.ParseResult("{\"error_code\":\"54003\",\"error_msg\":\"rate\"}"); Eq(false, true, "限流应抛"); }
                        catch (TranslateException ex) { Eq("rate_limited", ex.Code, "百度 54003 → rate_limited"); }
                        Eq("network", TranslationService.IsRetryable("network") ? "network" : "no", "可重试集合");
                        Eq(true, TranslationService.IsRetryable("rate_limited") && TranslationService.IsRetryable("server")
                            && TranslationService.IsRetryable("http"), "限流/服务端/HTTP 可重试");
                        Eq(false, TranslationService.IsRetryable("timeout") || TranslationService.IsRetryable("auth")
                            || TranslationService.IsRetryable("parse"), "超时/鉴权/解析不重试");
                    });

                    Section("ServiceRetryAndFallback", delegate
                    {
                        // 可重试错误：失败 2 次后第 3 次成功（指数退避路径）
                        var mm = new FakeProvider("mymemory",
                            new TranslateException("x", "rate_limited"),
                            new TranslateException("x", "network"),
                            "OK1");
                        var svc = MakeService(Cfg("mymemory", false), mm);
                        var r = Run(svc, "hello");
                        Eq(3, mm.Attempts, "第 3 次尝试成功");
                        Eq("OK1", r.TranslatedText, "重试后译文");
                        Eq("mymemory", r.Provider, "结果来自重试成功的 Provider");

                        // 超时：不重试，立即降级百度
                        var mm2 = new FakeProvider("mymemory", new TranslateException("t", "timeout"));
                        var bd2 = new FakeProvider("baidu", "百度译文");
                        var svc2 = MakeService(Cfg("mymemory", true), mm2, bd2);
                        var r2 = Run(svc2, "hello");
                        Eq(1, mm2.Attempts, "超时仅 1 次尝试");
                        Eq(1, bd2.Attempts, "降级到百度");
                        Eq("baidu", r2.Provider, "降级结果 Provider 可见");

                        // 鉴权失败：不重试，降级
                        var bd3 = new FakeProvider("baidu", new TranslateException("k", "auth"));
                        var mm3 = new FakeProvider("mymemory", "MyMemory 译文");
                        var svc3 = MakeService(Cfg("baidu", true), bd3, mm3);
                        var r3 = Run(svc3, "hello");
                        Eq("mymemory", r3.Provider, "百度鉴权失败降级 MyMemory");

                        // 双败：主错误为主文案 + 备用附注（server 可重试→脚本给满 3 次）
                        var mm4 = new FakeProvider("mymemory",
                            new TranslateException("主错误文案", "server"),
                            new TranslateException("主错误文案", "server"),
                            new TranslateException("主错误文案", "server"));
                        var bd4 = new FakeProvider("baidu", new TranslateException("备错误文案", "timeout"));
                        var svc4 = MakeService(Cfg("mymemory", true), mm4, bd4);
                        try { Run(svc4, "hello"); Eq(false, true, "双败应抛"); }
                        catch (TranslateException ex)
                        {
                            Eq("server", ex.Code, "双败保留主错误码");
                            True(ex.Message.Contains("主错误文案"), "主错误在前");
                            True(ex.Message.Contains("备用提供商也失败") && ex.Message.Contains("备错误文案"), "备用失败附注");
                        }

                        // 备用不可用（百度无密钥）：仅主 Provider，报主错误
                        var mm5 = new FakeProvider("mymemory", new TranslateException("只有主", "timeout"));
                        var svc5 = MakeService(Cfg("mymemory", false), mm5);
                        try { Run(svc5, "hello"); Eq(false, true, "应抛"); }
                        catch (TranslateException ex) { Eq("只有主", ex.Message, "无备用时主错误直抛"); }

                        // 取消不被降级吞掉
                        var mm6 = new FakeProvider("mymemory", "never");
                        var svc6 = MakeService(Cfg("mymemory", false), mm6);
                        bool cancelled = false;
                        try
                        {
                            var cts = new System.Threading.CancellationTokenSource();
                            cts.Cancel();
                            svc6.TranslateAsync(new TranslationRequest { Text = "x" }, cts.Token)
                                .GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException) { cancelled = true; }
                        Eq(true, cancelled, "取消原样上抛");
                    });

                    Section("ServiceCache", delegate
                    {
                        var mm = new FakeProvider("mymemory", "第一次译文");
                        var cfg = Cfg("mymemory", false);
                        var svc = MakeService(cfg, mm);
                        var r1 = Run(svc, "same text");
                        var r2 = Run(svc, "same text");   // 5min 内同语言对同文本
                        Eq(1, mm.Attempts, "第二次命中缓存不再请求");
                        Eq("第一次译文", r2.TranslatedText, "缓存译文一致");
                        Eq(0L, r2.ElapsedMs, "缓存命中近乎零耗时");
                        // 语言对不同 → 不命中缓存
                        var cfg2 = Cfg("mymemory", false); cfg2.TargetLang = "ja";
                        var svc2 = new TranslationService(delegate { return cfg2; },
                            delegate(AppConfig c, string id) { return mm; },
                            new ResultCache(TimeSpan.FromMinutes(5)));
                        Run(svc2, "same text");
                        Eq(2, mm.Attempts, "目标语言不同不串缓存");

                        // 回归（2026-09-03 用户实测）：切换 Provider 后必须真实重翻，
                        // 不得命中旧 Provider 的缓存结果；切回原 Provider 仍命中原缓存
                        var cfg3 = Cfg("mymemory", true);
                        var mm3 = new FakeProvider("mymemory", "MyMemory 译文");
                        var bd3 = new FakeProvider("baidu", "百度译文");
                        var svc3 = MakeService(cfg3, mm3, bd3);
                        var rA = Run(svc3, "same text");
                        Eq("mymemory", rA.Provider, "初始 MyMemory");
                        cfg3.Provider = "baidu";                        // 用户切到百度
                        var rB = Run(svc3, "same text");
                        Eq(1, bd3.Attempts, "切换 Provider 后真实重翻（不命中旧缓存）");
                        Eq("baidu", rB.Provider, "结果来自新 Provider");
                        Eq("百度译文", rB.TranslatedText, "新 Provider 译文");
                        cfg3.Provider = "mymemory";                     // 切回 MyMemory
                        var rC = Run(svc3, "same text");
                        Eq(1, mm3.Attempts, "切回原 Provider 仍命中原缓存");
                        Eq("MyMemory 译文", rC.TranslatedText, "原缓存译文");
                    });
                }
                finally
                {
                    TranslationService.RetryBackoffMs[0] = oldBackoff[0];
                    TranslationService.RetryBackoffMs[1] = oldBackoff[1];
                }
            }
        }

        // 优化 3：密钥 DPAPI 加密（CurrentUser 范围；明文兼容读、恒加密写）
        private static class SecretProtectionTests
        {
            public static void Register()
            {
                Section("SecretProtector round-trip", delegate
                {
                    string enc = SecretProtector.Protect("my-secret-value");
                    True(SecretProtector.IsProtected(enc), "密文带 dpapi: 前缀");
                    False(enc.Contains("my-secret-value"), "密文不含明文");
                    Eq("my-secret-value", SecretProtector.Unprotect(enc), "本机可解密回明文");

                    // 空值/明文值行为
                    Eq("", SecretProtector.Protect(""), "空串加密仍空（不包前缀）");
                    Eq("plain", SecretProtector.Unprotect("plain"), "无前缀=明文原样（旧版兼容）");
                    Eq("", SecretProtector.Unprotect(""), "空串解密安全");
                    Eq("", SecretProtector.Unprotect(null), "null 安全（返回空串）");
                    // 损坏密文 → 空串降级（换机/损坏场景，重存即恢复）
                    Eq("", SecretProtector.Unprotect("dpapi:not-base64!!"), "损坏密文降级空串");
                    Eq("", SecretProtector.Unprotect("dpapi:AAAA"), "无效密文降级空串");
                });

                Section("ConfigStore DPAPI on-disk", delegate
                {
                    string dir = Path.Combine(Path.GetTempPath(), "tfd-dpapi-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "config.conf");
                    try
                    {
                        // 写：落盘文件必须无明文密钥
                        var store = new ConfigStore(path);
                        True(store.WriteSync(new AppConfig
                        {
                            Hotkey = "^!t", Provider = "baidu", SourceLang = "en", TargetLang = "ja",
                            BaiduAppid = "appid-abc-123", BaiduSecret = "secret-xyz-987"
                        }), "写入成功");
                        string raw = File.ReadAllText(path, Encoding.UTF8);
                        False(raw.Contains("appid-abc-123"), "磁盘无明文 appid");
                        False(raw.Contains("secret-xyz-987"), "磁盘无明文 secret");
                        True(raw.Contains("baidu_appid=" + SecretProtector.Prefix), "appid 行为 dpapi 密文");
                        True(raw.Contains("baidu_secret=" + SecretProtector.Prefix), "secret 行为 dpapi 密文");

                        // 读：新实例解密回明文（业务可用）
                        var back = new ConfigStore(path).Current;
                        Eq("appid-abc-123", back.BaiduAppid, "回读明文 appid");
                        Eq("secret-xyz-987", back.BaiduSecret, "回读明文 secret");
                        True(back.HasBaiduKeys(), "HasBaiduKeys=true");

                        // 明文兼容：手工构造旧版明文文件 → 读出明文（升级期用户无感）
                        string legacy = "hotkey=^!d\nprovider=baidu\nsrc_lang=auto\ntgt_lang=zh-CN\n"
                            + "baidu_appid=legacy-appid\nbaidu_secret=legacy-secret\n";
                        File.WriteAllText(path, legacy, new UTF8Encoding(true));
                        var lg = new ConfigStore(path).Current;
                        Eq("legacy-appid", lg.BaiduAppid, "旧版明文文件可读");
                        Eq("legacy-secret", lg.BaiduSecret, "旧版明文 secret 可读");
                        // 对明文文件重新 WriteSync → 明文消失（迁移路径）
                        True(new ConfigStore(path).WriteSync(lg), "明文文件迁移回写");
                        False(File.ReadAllText(path, Encoding.UTF8).Contains("legacy-secret"), "迁移后磁盘无明文");

                        // 解密失败降级：密文有效但模拟跨机（本机 Unprotect 不败，
                        // 用损坏行验证降级链路 → HasBaiduKeys=false 不崩）
                        string corrupt = "hotkey=^!d\nprovider=mymemory\nbaidu_appid=dpapi:AAAA\nbaidu_secret=dpapi:BBBB\n";
                        File.WriteAllText(path, corrupt, new UTF8Encoding(true));
                        var cc = new ConfigStore(path).Current;
                        Eq("", cc.BaiduAppid, "损坏密文降级空 appid");
                        Eq("", cc.BaiduSecret, "损坏密文降级空 secret");
                        False(cc.HasBaiduKeys(), "降级后自动回 MyMemory 路径");
                    }
                    finally
                    {
                        try { Directory.Delete(dir, true); } catch (Exception) { }
                    }
                });
            }
        }

        // 优化 4：DeepL + OpenAI-compatible LLM（离线单测：解析/请求体/预设/保护器/门禁）
        private static class NewProviderTests
        {
            public static void Register()
            {
                Section("DeepL.ParseAndCodes", delegate
                {
                    Eq("你好世界", DeepLProvider.ParseResult(
                        "{\"translations\":[{\"detected_source_language\":\"EN\",\"text\":\"你好世界\"}]}"),
                        "translations[0].text");
                    TestRunner.Throws<TranslateException>(delegate { DeepLProvider.ParseResult("{}"); }, "空响应抛 parse");
                    Eq("EN", DeepLProvider.ToDeepLCode("en"), "小写转大写");
                    Eq("ZH", DeepLProvider.ToDeepLCode("zh-CN"), "zh-CN 特例");
                    Eq("ZH-TW", DeepLProvider.ToDeepLCode("zh-TW"), "zh-TW 特例");
                    Eq("JA", DeepLProvider.ToDeepLCode("ja"), "ja 直转");
                });

                Section("DeepL.FormBuild", delegate
                {
                    // 用反射不可取——直接走公开行为：构造 Provider 后无法拦截网络。
                    // V1 用 ToDeepLCode + 参数约定断言（form 构造在 TranslateAsync 内，
                    // 集成验证依赖真 key）；这里验证 auto 不产生 source_lang 的约定。
                    Eq("auto", DeepLProvider.ToDeepLCode("auto"), "auto 原样（调用方跳过 source_lang）");
                    // 端点规范化（用户实测踩坑：裸主机名缺 scheme/路径）
                    Eq(DeepLProvider.DefaultEndpoint, DeepLProvider.NormalizeEndpoint(""), "空=默认免费端点");
                    Eq(DeepLProvider.DefaultEndpoint, DeepLProvider.NormalizeEndpoint(null), "null=默认");
                    Eq("https://api-free.deepl.com/v2/translate",
                        DeepLProvider.NormalizeEndpoint("api-free.deepl.com"), "裸主机补 scheme+路径");
                    Eq("https://api-free.deepl.com/v2/translate",
                        DeepLProvider.NormalizeEndpoint("https://api-free.deepl.com"), "仅 scheme+主机补路径");
                    Eq("https://api.deepl.com/v2/translate",
                        DeepLProvider.NormalizeEndpoint("https://api.deepl.com/v2/translate"), "完整 URL 原样");
                    Eq("https://api.deepl.com/v2/translate",
                        DeepLProvider.NormalizeEndpoint("api.deepl.com"), "Pro 裸主机自动补全");
                });

                Section("LlmPresetCatalog", delegate
                {
                    Eq(6, ProviderCatalog.LlmPresets.Length, "预设数量（5 服务商+自定义）");
                    True(ProviderCatalog.FindLlmPreset("deepseek") != null
                        && ProviderCatalog.FindLlmPreset("deepseek").BaseUrl.Contains("deepseek.com"),
                        "DeepSeek 预设");
                    True(ProviderCatalog.FindLlmPreset("ollama").BaseUrl.StartsWith("http://127.0.0.1"),
                        "Ollama 本地端点");
                    True(ProviderCatalog.FindLlmPreset("custom") != null
                        && ProviderCatalog.FindLlmPreset("custom").BaseUrl.Length == 0,
                        "custom 空模板");
                    Eq(null, ProviderCatalog.FindLlmPreset("nope"), "未知预设 null");
                    True(ProviderCatalog.DefaultLlmPrompt.Contains("__TFD_G")
                        && ProviderCatalog.DefaultLlmPrompt.Contains("只返回译文"),
                        "默认 Prompt 含占位符规则与只返回译文要求");
                });

                Section("LlmRequestAndParse", delegate
                {
                    string json = OpenAICompatibleProvider.BuildRequestJson("test-model", "SYS", "USR");
                    True(json.Contains("\"model\":\"test-model\""), "请求体含 model");
                    True(json.Contains("\"role\":\"system\"") && json.Contains("SYS"), "system 消息");
                    True(json.Contains("\"role\":\"user\"") && json.Contains("USR"), "user 消息");
                    Eq("译文", OpenAICompatibleProvider.ParseResult(
                        "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\" 译文 \"}}]}"),
                        "choices[0].message.content（含两侧空白裁剪）");
                    TestRunner.Throws<TranslateException>(delegate { OpenAICompatibleProvider.ParseResult("{\"choices\":[]}"); },
                        "空 choices 抛 parse");
                    TestRunner.Throws<TranslateException>(delegate { OpenAICompatibleProvider.ParseResult(
                        "{\"error\":{\"message\":\"bad model\"}}"); }, "错误响应抛 parse");
                });

                Section("DevTextGuard", delegate
                {
                    // 高频场景：函数名+路径
                    var p1 = DevTextGuard.Protect("Call openai_client('/home/user/config.yaml')");
                    True(p1.Text.Contains("__TFD_G1__") || p1.Text.Contains("__TFD_G2__"),
                        "snake_case/路径命中占位符");
                    True(p1.Tokens.Count >= 2, "至少 2 个 token");
                    string back1 = DevTextGuard.Restore(p1.Text, p1.Tokens);
                    Eq("Call openai_client('/home/user/config.yaml')", back1, "恢复无损");

                    // 代码块 + 行内码 + URL
                    string src2 = "用 `npm run dev` 启动，见 https://example.com/docs 和 ```code block```";
                    var p2 = DevTextGuard.Protect(src2);
                    Eq(src2, DevTextGuard.Restore(p2.Text, p2.Tokens), "行内码/URL/围栏恢复无损");

                    // 普通英文不受影响（宁少勿错）
                    var p3 = DevTextGuard.Protect("The quick brown fox jumps over the lazy dog.");
                    Eq(0, p3.Tokens.Count, "普通英文零命中");
                    Eq("The quick brown fox jumps over the lazy dog.", p3.Text, "普通英文原样");

                    // CONSTANT_CASE
                    var p4 = DevTextGuard.Protect("set MAX_RETRY_COUNT to 3");
                    True(p4.Tokens.Count >= 1 && p4.Text.Contains("__TFD_G"), "常量命名命中");

                    // Windows 路径
                    var p5 = DevTextGuard.Protect("edit C:\\Users\\me\\app.config first");
                    True(p5.Tokens.Count >= 1 && p5.Text.Contains("__TFD_G"), "Windows 路径命中");
                    Eq("edit C:\\Users\\me\\app.config first",
                        DevTextGuard.Restore(p5.Text, p5.Tokens), "Win 路径恢复无损");

                    // 原文含占位符字样 → 跳过保护（防冲突）
                    var p6 = DevTextGuard.Protect("keep __TFD_G1__ literal");
                    Eq(0, p6.Tokens.Count, "原文含占位符字样跳过保护");

                    // 恢复失败检测：LLM 丢失占位符 → null（不静默）
                    Eq(null, DevTextGuard.Restore("译文丢了 __TFD_G2__ 和 __TFD_G1__",
                        new List<KeyValuePair<string, string>> { 
                            new KeyValuePair<string, string>("__TFD_G1__", "openai_client") }),
                        "占位符缺失恢复失败返回 null");
                    // LLM 改写占位符大小写 → 检测失败
                    Eq(null, DevTextGuard.Restore("changed __tfd_g1__ case",
                        new List<KeyValuePair<string, string>> {
                            new KeyValuePair<string, string>("__TFD_G1__", "x") }),
                        "大小写改写检测失败");
                });

                Section("ProviderGateAndCatalog", delegate
                {
                    // 门禁：未配置的 Provider 主落到 mymemory（可用性兜底）
                    var cfg = new AppConfig { Provider = "deepl", SourceLang = "en", TargetLang = "zh-CN" };
                    Eq("mymemory", typeof(TranslationService)
                        .GetMethod("ResolvePrimaryId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .Invoke(null, new object[] { cfg }) as string, "deepl 无 key 主落 mymemory");
                    cfg.Provider = "llm";
                    Eq("mymemory", typeof(TranslationService)
                        .GetMethod("ResolvePrimaryId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .Invoke(null, new object[] { cfg }) as string, "llm 未配置主落 mymemory");
                    cfg.LlmBaseUrl = "http://x/v1"; cfg.LlmModel = "m1";
                    Eq("llm", typeof(TranslationService)
                        .GetMethod("ResolvePrimaryId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .Invoke(null, new object[] { cfg }) as string, "llm 配置齐备生效");
                    // 工厂：deepl 有 key → DeepLProvider；llm 齐备 → OpenAICompatibleProvider
                    cfg.DeeplKey = "k"; cfg.Provider = "deepl";
                    var f = typeof(TranslationService)
                        .GetMethod("DefaultFactory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var dp = f.Invoke(null, new object[] { cfg, "deepl" }) as ITranslationProvider;
                    Eq("deepl", dp.Id, "工厂产出 DeepLProvider");
                    cfg.Provider = "llm"; cfg.LlmApiKey = "sk";
                    var lp = f.Invoke(null, new object[] { cfg, "llm" }) as ITranslationProvider;
                    Eq("llm", lp.Id, "工厂产出 OpenAICompatibleProvider");
                    // 显示名目录
                    Eq("DeepL", ProviderCatalog.DisplayName("deepl"), "DeepL 显示名");
                    Eq("AI 大模型", ProviderCatalog.DisplayName("llm"), "LLM 显示名");
                    // 4 密钥行 DPAPI：AppConfig 扩展字段经 ConfigStore 往返
                    string dir = Path.Combine(Path.GetTempPath(), "tfd-np-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    string cpath = Path.Combine(dir, "config.conf");
                    try
                    {
                        var st = new ConfigStore(cpath);
                        True(st.WriteSync(new AppConfig
                        {
                            Hotkey = "^!t", Provider = "llm", SourceLang = "auto", TargetLang = "zh-CN",
                            BaiduAppid = "ba", BaiduSecret = "bs",
                            DeeplKey = "dk-1", DeeplEndpoint = "",
                            LlmPreset = "custom", LlmBaseUrl = "http://127.0.0.1:9999/v1",
                            LlmApiKey = "lk-2", LlmModel = "m-x", LlmPrompt = "P1"
                        }), "13 键写入");
                        string raw = File.ReadAllText(cpath, Encoding.UTF8);
                        False(raw.Contains("dk-1") || raw.Contains("lk-2"), "新密钥 DPAPI 加密落盘");
                        var rt = new ConfigStore(cpath).Current;
                        Eq("dk-1", rt.DeeplKey, "DeeplKey 回读");
                        Eq("http://127.0.0.1:9999/v1", rt.LlmBaseUrl, "LlmBaseUrl 回读");
                        Eq("lk-2", rt.LlmApiKey, "LlmApiKey 回读");
                        Eq("m-x", rt.LlmModel, "LlmModel 回读");
                        Eq("custom", rt.LlmPreset, "LlmPreset 回读");
                    }
                    finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
                });
            }
        }
    }
}
