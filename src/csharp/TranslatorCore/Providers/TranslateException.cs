//=============================================================
// Providers/TranslateException.cs - 业务翻译异常
// Message = 用户可直接展示的中文文案（经协议 error 帧
//   {code:"translate_failed", message} 到达页面，UI 不解析原始 JSON）
//=============================================================
using System;

namespace Translator.Core.Providers
{
    public sealed class TranslateException : Exception
    {
        public TranslateException(string message) : base(message) { }
    }
}
