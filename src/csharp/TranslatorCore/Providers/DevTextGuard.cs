//=============================================================
// Providers/DevTextGuard.cs - 开发者内容 placeholder 保护（V1，仅 LLM Provider 用）
// 目标：LLM 翻译前把「明显不该翻译」的高频低危 token 换成唯一占位符，
//   译后恢复。V1 范围（简单可靠优先，非 AST）：
//   ```围栏代码块``` / `行内码` / URL / Windows 路径 / Unix 路径 /
//   带常见扩展名的文件名 / CONSTANT_CASE / snake_case / lowerCamelCase(含点号链)。
// 设计约束：
//   - 占位符 __TFD_G<序号>__ 唯一且稳定（序号 = 捕获顺序，恢复确定性）
//   - 零命中 = 原样返回（普通英文句子不受任何影响）
//   - 恢复前逐一校验占位符存在；LLM 改写/丢失占位符 → 抛 TranslateException
//     （不静默返回坏结果，调用方走重试/降级）
//   - 原文本身含 __TFD_G 字样时直接跳过保护（避免占位符冲突，概率可忽略）
//=============================================================
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Translator.Core.Providers
{
    public static class DevTextGuard
    {
        private const string TokenPrefix = "__TFD_G";
        private const string TokenSuffix = "__";

        // 顺序即优先级：围栏块最外层先剥，避免块内内容再被行内规则切碎。
        // 行内规则一律 \b 或显式边界，避免把普通单词误保护（宁少勿错）。
        private static readonly Regex Fence = new Regex("```[\\s\\S]*?```|~~~[\\s\\S]*?~~~", RegexOptions.Compiled);
        private static readonly Regex InlineCode = new Regex("`[^`\\n]+`", RegexOptions.Compiled);
        private static readonly Regex Url = new Regex(
            "https?://[^\\s\\)\\]}>'\"，。；！？]+|www\\.[^\\s\\)\\]}>'\"，。；！？]+", RegexOptions.Compiled);
        private static readonly Regex WinPath = new Regex(
            "[A-Za-z]:\\\\[^\\s\"'，。；！？]{2,}", RegexOptions.Compiled);
        private static readonly Regex UnixPath = new Regex(
            "(?<![\\w~])/(?:usr|home|etc|var|opt|tmp|root|dev|proc|bin|sbin|lib|mnt|media|srv|data)(?:/[^\\s\"'，。；！？]+)+", RegexOptions.Compiled);
        private static readonly Regex FileName = new Regex(
            "\\b[\\w\\-]+\\.(?:js|ts|tsx|jsx|py|java|c|h|cpp|cs|go|rs|rb|php|swift|kt|sh|bat|ps1|yaml|yml|json|xml|toml|ini|md|txt|csv|sql|html|css|conf|lock|env)\\b", RegexOptions.Compiled);
        private static readonly Regex ConstCase = new Regex(
            "\\b[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+\\b", RegexOptions.Compiled);
        private static readonly Regex SnakeCase = new Regex(
            "\\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\\b", RegexOptions.Compiled);
        private static readonly Regex CamelChain = new Regex(
            "\\b[a-z]+(?:[A-Z][a-z0-9]+){1,}(?:\\.[a-z]+(?:[A-Z][a-z0-9]+){1,})*\\(?", RegexOptions.Compiled);

        /// <summary>保护结果：Text=替换后文本；Tokens=占位符→原文。</summary>
        public sealed class Protected
        {
            public string Text;
            public List<KeyValuePair<string, string>> Tokens;
        }

        /// <summary>保护：命中 token 逐一替换为 __TFD_G1__..；零命中原样。</summary>
        public static Protected Protect(string text)
        {
            var r = new Protected { Text = text, Tokens = new List<KeyValuePair<string, string>>() };
            if (string.IsNullOrEmpty(text) || text.Contains(TokenPrefix))
                return r;   // 原文已含占位符字样：跳过保护（防冲突）
            var sb = new StringBuilder(text);
            ProtectRegex(sb, Fence, r, keepDelims: true);
            ProtectRegex(sb, InlineCode, r, keepDelims: true);
            ProtectRegex(sb, Url, r, false);
            ProtectRegex(sb, WinPath, r, false);
            ProtectRegex(sb, UnixPath, r, false);
            ProtectRegex(sb, FileName, r, false);
            ProtectRegex(sb, ConstCase, r, false);
            ProtectRegex(sb, SnakeCase, r, false);
            ProtectRegex(sb, CamelChain, r, false);
            r.Text = sb.ToString();
            return r;
        }

        /// <summary>单规则扫描替换：每命中一个占位符（已替换区域不再扫——
        /// 用游标跳过 __TFD_G*__ 区间）。</summary>
        private static void ProtectRegex(StringBuilder sb, Regex re, Protected state, bool keepDelims)
        {
            var matches = re.Matches(sb.ToString());
            int skipFrom = -1;
            foreach (Match m in matches)
            {
                if (m.Length == 0) continue;
                if (m.Index >= skipFrom && InPlaceholder(sb.ToString(), m.Index))
                    continue;
                string raw = m.Value;
                string val = keepDelims ? raw : raw;
                string token = TokenPrefix + (state.Tokens.Count + 1) + TokenSuffix;
                state.Tokens.Add(new KeyValuePair<string, string>(token, val));
                sb.Remove(m.Index, m.Length);
                sb.Insert(m.Index, token);
                skipFrom = m.Index + token.Length;
            }
        }

        /// <summary>位置是否落在已生成的 __TFD_G*__ 占位符内。</summary>
        private static bool InPlaceholder(string s, int index)
        {
            int p = s.LastIndexOf(TokenPrefix, index, StringComparison.Ordinal);
            if (p < 0) return false;
            int end = s.IndexOf(TokenSuffix, p, StringComparison.Ordinal);
            return end >= p && index <= end + TokenSuffix.Length;
        }

        /// <summary>恢复：按 Tokens 逐一替换回原文。两类失败均返回 null
        /// （调用方按失败处理走重试/降级，绝不静默返回坏译文）：
        ///   ① 已知占位符在译文中缺失/被改写；
        ///   ② 恢复完成后仍残留 __TFD_G*__（LLM 幻觉出的未知占位符）。</summary>
        public static string Restore(string translated, List<KeyValuePair<string, string>> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return translated;
            string s = translated;
            foreach (var kv in tokens)
            {
                if (s.IndexOf(kv.Key, StringComparison.Ordinal) < 0)
                    return null;   // ① LLM 丢改占位符：恢复失败
                s = s.Replace(kv.Key, kv.Value);
            }
            if (Leftover.IsMatch(s))
                return null;       // ② 残留未知占位符（幻觉 token）
            return s;
        }

        private static readonly Regex Leftover = new Regex("__TFD_G\\d+__", RegexOptions.Compiled);
    }
}
