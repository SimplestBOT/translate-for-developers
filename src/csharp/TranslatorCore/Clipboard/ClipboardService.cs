//=============================================================
// Clipboard/ClipboardService.cs - 剪贴板（阶段 3 接管 copy 场景）
// 阶段 3 范围：结果页「复制」按钮 → SetText（宿主 STA UI 线程调用）。
// TranslateSelected 的「备份/恢复/ClipWait 选中捕获」依赖热键触发链路，
// 随阶段 5 Hotkey 一并迁移（届时落在本类旁）。
// 注意：命名空间Translator.Core.Clipboard 与 WinForms Clipboard 类同名，
//       WinForms 类型一律完全限定，不做 using 引入。
//=============================================================
using System.Windows.Forms;

namespace Translator.Core.Clipboard
{
    public static class ClipboardService
    {
        /// <summary>写文本到剪贴板。必须在 STA 线程（宿主 UI 线程）调用。</summary>
        /// <remarks>
        /// System.Windows.Forms.Clipboard.SetText 内部自带 OLE 重试（剪贴板被占用场景）；
        /// 仍可能抛 ExternalException（如被独占锁死），调用方 catch 后仅记录。
        /// </remarks>
        public static void SetText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            System.Windows.Forms.Clipboard.SetText(text);
        }

        /// <summary>读取当前剪贴板文本（非文本/空返回 null）。STA 线程调用。</summary>
        public static string GetText()
        {
            if (!System.Windows.Forms.Clipboard.ContainsText())
                return null;
            string t = System.Windows.Forms.Clipboard.GetText();
            return t.Length > 0 ? t : null;
        }
    }
}
