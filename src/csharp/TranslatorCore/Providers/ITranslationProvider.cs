//=============================================================
// Providers/ITranslationProvider.cs - Provider 抽象
// 语义：TranslateAsync 接收「全文」，Provider 内部按自己的配额
//   策略分片（MyMemory=字符数 / 百度=UTF-8 字节数），逐片请求并拼接。
//   分片循环每片检查 ct.ThrowIfCancellationRequested()（硬约束）。
// 组合层（TranslationService）只做：Provider 选择 + 计时 + 结果组装。
//=============================================================
using System.Threading;
using System.Threading.Tasks;

namespace Translator.Core.Providers
{
    public interface ITranslationProvider
    {
        /// <summary>provider 标识（协议统一模型 provider 字段）：mymemory / baidu</summary>
        string Id { get; }

        /// <summary>显示名（中文 UI）：MyMemory / 百度翻译</summary>
        string DisplayName { get; }

        /// <summary>
        /// 翻译全文（内部分片循环）。约定异常：
        ///   取消 → OperationCanceledException（原样上抛，不吞）；
        ///   业务/网络失败 → TranslateException（message 为用户可直接展示的文案）。
        /// </summary>
        Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct);
    }
}
