//=============================================================
// Protocol.cs - 消息契约（与 docs/protocol.md 对齐）
// 页面信封: {v, type, requestId, payload}——Transport 无关，本文件只定义
// 常量与信封组装，不做任何 IO。
// 历史：阶段 3 minor 0→1（config_set/hello.configPath/init.testError）、
// 阶段 5a minor 1→2（hotkey_* 五消息）、阶段 6 minor 2→3（AHK 退役）。
// 阶段 7：Named Pipe 桥测试面整体删除（hello/open_window/push/config_set/
// close_window/hotkey_*/page_event 透传），宿主仅存页面级信封——页面↔宿主
// 协议（§2/§3/§4）自 v1.1 起未变，major 保持 1。
//=============================================================
namespace TranslatorHost
{
    public static class Protocol
    {
        public const int Major = 1;
        public const int Minor = 4;

        /// <summary>组装页面级信封（直接推给页面的对象），requestId 透传配对</summary>
        public static string PageEnvelope(string type, string payloadJson, int requestId)
        {
            return "{\"v\":1,\"type\":\"" + type + "\",\"requestId\":" + requestId + ",\"payload\":"
                + (string.IsNullOrEmpty(payloadJson) ? "null" : payloadJson) + "}";
        }

        // 页面错误码（error 帧 payload.code，UI 渲染统一错误态）
        public const string ETranslateFailed = "translate_failed";
    }
}
