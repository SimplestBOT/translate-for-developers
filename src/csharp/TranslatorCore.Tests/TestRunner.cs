//=============================================================
// TestRunner.cs - 无依赖断言 runner（离线可跑，免 xUnit 包还原）
// 退出码 0=全绿 1=失败；协议.md §7：payload 示例即一致性测试用例来源
//=============================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace Translator.Core.Tests
{
    internal static class TestRunner
    {
        private static int _failCount;
        private static int _passCount;
        private static string _current;

        private static void Check(bool cond, string what)
        {
            if (cond)
            {
                _passCount++;
            }
            else
            {
                _failCount++;
                Console.WriteLine("  FAIL [" + (_current ?? "?") + "] " + what);
            }
        }

        public static void Section(string name, Action body)
        {
            _current = name;
            Console.WriteLine("[" + name + "]");
            try { body(); }
            catch (Exception ex)
            {
                _failCount++;
                Console.WriteLine("  THROW [" + name + "] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void Eq(object expect, object actual, string what)
        {
            Check(Equals(expect, actual), what + " (expect=" + Show(expect) + " actual=" + Show(actual) + ")");
        }

        public static void True(bool cond, string what) { Check(cond, what); }
        public static void False(bool cond, string what) { Check(!cond, what); }

        public static T Throws<T>(Action body, string what) where T : Exception
        {
            try { body(); Check(false, what + " (no exception)"); }
            catch (T) { Check(true, what); }
            catch (Exception ex)
            {
                Check(ex is T, what + " (wrong exception " + ex.GetType().Name + ")");
            }
            return null;
        }

        private static string Show(object o)
        {
            var s = o as string;
            return o == null ? "null" : (s != null && s.Length > 60 ? "\"" + s.Substring(0, 57) + "...\"" : "\"" + o + "\"");
        }

        public static int ExitCode { get { return _failCount == 0 ? 0 : 1; } }

        public static string Summary { get { return _passCount + " passed, " + _failCount + " failed"; } }

        public static List<string> RunAll()
        {
            // 占位：Main 里按序注册各 Section
            return new List<string>();
        }

        public static void Require(bool cond, string setup)
        {
            if (!cond) throw new Exception("测试前置失败: " + setup);
        }
    }
}
