//=============================================================
// Configuration/LanguageTable.cs - 语言表（与 translator.ahk gLangs 对齐）
// 阶段 3 C# 需要：ID 合法性校验（配置写方校验）+ 百度代码映射（BaiduProvider）。
// 阶段 5b：显示名迁入（gLangs 名称列，宿主独立运行时设置页 init / 托盘菜单由
// C# 组装——此前由 AHK SettingsInitJson 维护）。
//=============================================================
using System.Collections.Generic;

namespace Translator.Core.Configuration
{
    public static class LanguageTable
    {
        // ID => 百度 API 代码（源/目标语言通用；"auto" 为百度原生自动检测）
        private static readonly Dictionary<string, string> BaiduCodes = new Dictionary<string, string>
        {
            { "auto", "auto" },
            { "zh-CN", "zh" },   { "zh-TW", "cht" },  { "en", "en" },
            { "ja", "jp" },      { "ko", "kor" },     { "fr", "fra" },
            { "de", "de" },      { "es", "spa" },     { "pt", "pt" },
            { "ru", "ru" },      { "it", "it" },      { "ar", "ara" },
            { "hi", "hi" },      { "th", "th" },      { "vi", "vi" },
            { "id", "id" },      { "tr", "tr" },      { "nl", "nl" },
            { "pl", "pl" },      { "uk", "uk" },      { "el", "el" },
            { "cs", "cs" },      { "sv", "sv" },      { "hu", "hu" },
            { "ro", "ro" },      { "da", "da" },      { "fi", "fi" },
            { "no", "no" },      { "ms", "ms" },      { "fil", "fil" },
            { "bn", "bn" },      { "ur", "ur" },      { "fa", "fa" },
            { "he", "iw" }
        };

        // ID => 显示名（translator.ahk gLangs 名称列，顺序即 Order）
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "auto", "自动检测" },  { "zh-CN", "简体中文" }, { "zh-TW", "繁体中文" },
            { "en", "英语" },        { "ja", "日语" },        { "ko", "韩语" },
            { "fr", "法语" },        { "de", "德语" },        { "es", "西班牙语" },
            { "pt", "葡萄牙语" },    { "ru", "俄语" },        { "it", "意大利语" },
            { "ar", "阿拉伯语" },    { "hi", "印地语" },      { "th", "泰语" },
            { "vi", "越南语" },      { "id", "印尼语" },      { "tr", "土耳其语" },
            { "nl", "荷兰语" },      { "pl", "波兰语" },      { "uk", "乌克兰语" },
            { "el", "希腊语" },      { "cs", "捷克语" },      { "sv", "瑞典语" },
            { "hu", "匈牙利语" },    { "ro", "罗马尼亚语" },  { "da", "丹麦语" },
            { "fi", "芬兰语" },      { "no", "挪威语" },      { "ms", "马来语" },
            { "fil", "菲律宾语" },   { "bn", "孟加拉语" },    { "ur", "乌尔都语" },
            { "fa", "波斯语" },      { "he", "希伯来语" }
        };

        /// <summary>语言 ID 顺序（与 gLangs 一致；设置页 init / 托盘菜单遍历用）。</summary>
        public static readonly string[] Order = new string[]
        {
            "auto", "zh-CN", "zh-TW", "en", "ja", "ko", "fr", "de", "es",
            "pt", "ru", "it", "ar", "hi", "th", "vi", "id", "tr", "nl",
            "pl", "uk", "el", "cs", "sv", "hu", "ro", "da", "fi", "no",
            "ms", "fil", "bn", "ur", "fa", "he"
        };

        public static string DisplayName(string id)
        {
            string name;
            if (id != null && Names.TryGetValue(id, out name))
                return name;
            return id ?? "";
        }

        public static bool IsAuto(string id) { return id == "auto"; }

        public static bool IsKnown(string id)
        {
            return id != null && BaiduCodes.ContainsKey(id);
        }

        public static string ToBaiduCode(string id)
        {
            string code;
            if (id != null && BaiduCodes.TryGetValue(id, out code))
                return code;
            return id; // 未知 ID 原样传（AHK BaiduLang 同语义）
        }
    }
}
