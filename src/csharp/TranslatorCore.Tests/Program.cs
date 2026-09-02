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
                        string content = File.ReadAllText(path, Encoding.UTF8);
                        True(content.Contains("hotkey=^!d"), "含 hotkey");
                        True(content.Contains("baidu_secret=s=ecret\"quote"), "含原始密钥（含特殊字符不转义）");
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
    }
}
