//=============================================================
// UiaText.cs - UIA 选区候选文本判定（纯函数，可单测）
// 无 P/Invoke、无 UIA 依赖；宿主 UiaSelectionProvider 取到原始选区
// 文本后经此判定有效性（P1：UIA 选区直读，见 SelectionProviders.cs）。
//=============================================================
using System;

namespace Translator.Core.Infrastructure
{
    public static class UiaText
    {
        /// <summary>选区候选上限（字符）。划词翻译场景足够；部分 provider 缺陷
        /// 场景可能返回全文档，截断防巨型文本拖慢翻译链路。</summary>
        public const int MaxChars = 8192;

        /// <summary>候选有效性判定：null/空/纯空白 = 无有效选区（返回 null）；
        /// 正常文本原样返回（超过 MaxChars 截断）。不 Trim——与剪贴板捕获路径
        /// 行为一致（首尾空白交由页面侧展示/分片处理）。</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (raw.Length > MaxChars) raw = raw.Substring(0, MaxChars);
            return raw;
        }
    }
}
