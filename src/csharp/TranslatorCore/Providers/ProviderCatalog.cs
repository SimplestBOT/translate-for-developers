//=============================================================
// Providers/ProviderCatalog.cs - Provider 目录（显示名 + LLM 预设模板）
// 显示名：设置页/结果窗/托盘菜单统一从这里取（不再散落三元表达式）。
// LLM 预设：仅「配置模板」（BaseUrl + Model），不是独立 Provider——
//   运行时唯一实现是 OpenAICompatibleProvider；UI 选预设=表单填充，
//   落盘字段为最终事实（LlmPreset 字段仅记录来源供 UI 回显）。
// custom 预设 = 用户自填 Base URL / Model / API Key（表为空模板）。
//=============================================================
using System.Collections.Generic;

namespace Translator.Core.Providers
{
    public static class ProviderCatalog
    {
        /// <summary>Provider 显示名（协议 provider 字段 → UI 文案）</summary>
        public static string DisplayName(string id)
        {
            switch (id)
            {
                case "baidu": return "百度翻译";
                case "deepl": return "DeepL";
                case "llm": return "AI 大模型";
                default: return "MyMemory";
            }
        }

        public sealed class LlmPreset
        {
            public string Id;
            public string Name;
            public string BaseUrl;
            public string Model;
        }

        /// <summary>内置预设（模板；模型名用户可改——Ollama 取决于本地已拉取模型）。
        /// BaseUrl 均到 /v1 或等价根（endpoint 由 Provider 拼 /chat/completions）。</summary>
        public static readonly LlmPreset[] LlmPresets = new LlmPreset[]
        {
            new LlmPreset { Id = "openai",   Name = "OpenAI",   BaseUrl = "https://api.openai.com/v1",            Model = "gpt-4o-mini" },
            new LlmPreset { Id = "deepseek", Name = "DeepSeek", BaseUrl = "https://api.deepseek.com/v1",          Model = "deepseek-chat" },
            new LlmPreset { Id = "kimi",     Name = "Kimi",     BaseUrl = "https://api.moonshot.cn/v1",           Model = "moonshot-v1-8k" },
            new LlmPreset { Id = "zhipu",    Name = "智谱",     BaseUrl = "https://open.bigmodel.cn/api/paas/v4", Model = "glm-4-flash" },
            new LlmPreset { Id = "ollama",   Name = "Ollama",   BaseUrl = "http://127.0.0.1:11434/v1",            Model = "qwen2.5:7b" },
            new LlmPreset { Id = "custom",   Name = "自定义",   BaseUrl = "",                                     Model = "" },
        };

        /// <summary>按 Id 查预设；未知返回 null。</summary>
        public static LlmPreset FindLlmPreset(string id)
        {
            foreach (var p in LlmPresets)
                if (p.Id == id) return p;
            return null;
        }

        /// <summary>LLM 默认 Prompt（config.llm_prompt 为空时使用；可经配置覆盖，
        /// 后续 UI 可编辑）。占位符规则与 DevTextGuard 的 __TFD_G*__ 对应。</summary>
        public const string DefaultLlmPrompt =
            "你是专业技术翻译引擎。把用户输入的内容从源语言翻译为目标语言。规则：\n" +
            "1. 只返回译文，不要任何解释、前言、后缀或引号。\n" +
            "2. 保持原始格式：换行、空格、Markdown 语法、列表与标点结构。\n" +
            "3. 代码、命令、变量名、函数名、类名、文件路径、URL 一律保留原样，不要翻译或改写。\n" +
            "4. __TFD_G数字__ 形式的占位符必须原样保留，禁止翻译、改写、增删或调整大小写。\n" +
            "5. 译文自然、准确，符合技术文档语境。";
    }
}
