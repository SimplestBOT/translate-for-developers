//=============================================================
// Ocr/OcrText.cs - OCR 纯函数（语言匹配 / 尺寸适配 / 行拼装）（优化 5）
// 与 WinRT 类型零耦合——Core 测试直接覆盖；OcrService 内部才触达 WinRT。
//=============================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace Translator.Core.Ocr
{
    public static class OcrText
    {
        /// <summary>
        /// 翻译源语言 → OCR 识别语言选择。优先级：
        ///   auto/空 → profileFirst（用户系统语言引擎）；
        ///   全 tag 精确 → zh 族（zh-cn→zh-hans、zh-tw→zh-hant，目标族缺失时
        ///   任何 zh 引擎兜底——hans 引擎读繁体输出简体，可读性强于失败）；
        ///   主子标签前缀（en→en-US）→ 主标签前缀（en-US→en-GB 近亲）；
        ///   全不中 → profileFirst 兜底（识别质量略降但强于直接失败——
        ///   OCR 语言包缺失时可选面本来就窄）。可用列表空 → null（调用方报
        ///   「未安装 OCR 语言包」）。
        /// </summary>
        public static string PickLanguage(string srcLang, IList<string> availableTags, string profileFirst)
        {
            if (availableTags == null || availableTags.Count == 0) return null;
            string src = (srcLang ?? "").Trim().ToLowerInvariant();
            if (src.Length == 0 || src == "auto")
                return profileFirst ?? availableTags[0];

            foreach (string tag in availableTags)
                if (tag.ToLowerInvariant() == src) return tag;

            if (src == "zh-cn" || src == "zh-sg" || src == "zh-hans"
                || src == "zh-tw" || src == "zh-hk" || src == "zh-mo" || src == "zh-hant")
            {
                bool hant = src == "zh-tw" || src == "zh-hk" || src == "zh-mo" || src == "zh-hant";
                string family = hant ? "zh-hant" : "zh-hans";
                foreach (string tag in availableTags)
                {
                    string t = tag.ToLowerInvariant();
                    if (t == family || t.StartsWith(family + "-")) return tag;
                }
                foreach (string tag in availableTags)
                    if (tag.ToLowerInvariant().StartsWith("zh")) return tag;
            }
            else
            {
                int dash = src.IndexOf('-');
                string main = dash > 0 ? src.Substring(0, dash) : src;
                foreach (string tag in availableTags)
                {
                    string t = tag.ToLowerInvariant();
                    if (t == src || t.StartsWith(src + "-")) return tag;
                }
                foreach (string tag in availableTags)
                {
                    string t = tag.ToLowerInvariant();
                    if (t == main || t.StartsWith(main + "-")) return tag;
                }
            }
            return profileFirst ?? availableTags[0];
        }

        /// <summary>超过 maxDim 时等比缩小（OcrEngine.MaxImageDimension 硬限；
        /// 截图正常远低于上限，超限只出现在整屏高 DPI 拼图等极端场景）。</summary>
        public static void FitDimension(int w, int h, long maxDim, out int nw, out int nh)
        {
            nw = w; nh = h;
            if (w <= 0 || h <= 0 || maxDim <= 0) return;
            if (w <= maxDim && h <= maxDim) return;
            double scale = Math.Min((double)maxDim / w, (double)maxDim / h);
            nw = Math.Max(1, (int)Math.Round(w * scale));
            nh = Math.Max(1, (int)Math.Round(h * scale));
        }

        /// <summary>OCR 行拼装：行尾空白剥离、\r\n 连接。保留行结构——报错
        /// 堆栈/表格类截图翻译时换行对齐比合并成整段更可读。</summary>
        public static string JoinLines(IEnumerable<string> lines)
        {
            if (lines == null) return "";
            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                if (line == null) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(line.TrimEnd());
            }
            return sb.ToString();
        }

        /// <summary>暗底浅字（深色 IDE/终端截图）判定：引擎为白底黑字文档优化，
        /// 反色内容识别率暴跌——区域平均亮度 &lt; 128 视为暗背景，先反色再喂。
        /// 背景在截图中占多数像素，均值即代表背景明暗。</summary>
        public static bool ShouldInvert(double avgLuma)
        {
            return avgLuma >= 0 && avgLuma < 128;
        }

        /// <summary>小区域放大判定：引擎对字高 ~20px 以上效果最好；截图区域
        /// 高度 &lt; 160px 时整图 2x 放大（单行报错/字幕条场景）。上限由
        /// FitDimension 先行保证，放大后不会触及 MaxImageDimension。</summary>
        public static bool ShouldUpscale(int w, int h)
        {
            return w > 0 && h > 0 && h < 160;
        }
    }
}
