//=============================================================
// Providers/TextSplitter.cs - 长文本分片
// 从 translator.ahk SplitTextByChars / SplitTextByBytes 逐行移植，
// 语义完全对齐（含 AHK 的边界行为）：
//   - 按行累积，超出配额时在「行边界」切分
//   - 超长单行（> 配额）在行内按字符/字节硬切
//   - 行内硬切处不做特殊处理（与 AHK 一致，切点即拼接点）
//=============================================================
using System.Collections.Generic;
using System.Text;

namespace Translator.Core.Providers
{
    public static class TextSplitter
    {
        /// <summary>按字符数分片（MyMemory 免费 API 单次限 500 字符，安全值 450）</summary>
        public static List<string> SplitByChars(string text, int maxChars)
        {
            var parts = new List<string>();
            string current = "";
            foreach (var line in SplitLines(text))
            {
                if (line.Length > maxChars)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current);
                        current = "";
                    }
                    foreach (var ch in line)
                    {
                        if (current.Length + 1 > maxChars && current.Length > 0)
                        {
                            parts.Add(current);
                            current = "";
                        }
                        current += ch;
                    }
                    if (current.Length > 0)
                    {
                        parts.Add(current);
                        current = "";
                    }
                }
                else
                {
                    if (current.Length == 0)
                        current = line;
                    else
                        current += "\n" + line;
                    if (current.Length > maxChars)
                    {
                        parts.Add(current);
                        current = "";
                    }
                }
            }
            if (current.Length > 0)
                parts.Add(current);
            return parts;
        }

        /// <summary>按 UTF-8 字节数分片（百度标准版单次 q 限 6000 字节，安全值 5000）</summary>
        public static List<string> SplitByBytes(string text, int maxBytes)
        {
            var parts = new List<string>();
            string current = "";
            int currBytes = 0;
            foreach (var line in SplitLines(text))
            {
                int lineBytes = Utf8Len(line);
                if (lineBytes > maxBytes)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current);
                        current = "";
                        currBytes = 0;
                    }
                    foreach (var ch in line)
                    {
                        int b = Utf8Len(ch.ToString());
                        if (currBytes + b > maxBytes && current.Length > 0)
                        {
                            parts.Add(current);
                            current = ch.ToString();
                            currBytes = b;
                        }
                        else
                        {
                            current += ch;
                            currBytes += b;
                        }
                    }
                    if (current.Length > 0)
                    {
                        parts.Add(current);
                        current = "";
                        currBytes = 0;
                    }
                }
                else
                {
                    int addBytes = lineBytes + (current.Length == 0 ? 0 : 1); // +1 = 行间 \n
                    if (currBytes + addBytes > maxBytes && current.Length > 0)
                    {
                        parts.Add(current);
                        current = line;
                        currBytes = lineBytes;
                    }
                    else
                    {
                        if (current.Length == 0)
                            current = line;
                        else
                            current += "\n" + line;
                        currBytes += addBytes;
                    }
                }
            }
            if (current.Length > 0)
                parts.Add(current);
            return parts;
        }

        private static int Utf8Len(string s)
        {
            return Encoding.UTF8.GetByteCount(s);
        }

        /// <summary>与 AHK StrSplit(text, "`n") 对齐：仅按 \n 切、保留行内 \r、空串得 [""]</summary>
        private static List<string> SplitLines(string text)
        {
            if (text == null)
                text = "";
            return new List<string>(text.Split('\n'));
        }
    }
}
