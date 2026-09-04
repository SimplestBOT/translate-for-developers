//=============================================================
// Configuration/SecretProtector.cs - 百度密钥 DPAPI 加密（优化 3）
// 格式：dpapi:<base64(CurrentUser|ProtectedData.Protect(UTF8(secret)))>
// CurrentUser 范围 = 仅本机当前 Windows 账户可解密（换机/换账户/拷贝
//   config.conf 出去都拿不到明文）；文件本身仍在本机 scripts\ 下。
// 兼容策略（ConfigStore 调用）：
//   读：enc 前缀 → 解密；非前缀 → 原样（明文兼容，旧版文件可读）。
//   写：恒加密（明文值首次写盘后即消失）。
//   解密失败（文件被拷到别的机器/账户）→ 空串降级，用户重存一次密钥即恢复，
//     不崩宿主。熵（entropy）不使用：跨 .NET 版本/实现仍可互解，密钥安全
//     完全由 DPAPI 用户凭据承载。
//=============================================================
using System;
using System.Security.Cryptography;
using System.Text;

namespace Translator.Core.Configuration
{
    public static class SecretProtector
    {
        /// <summary>密文值前缀（config.conf 行内标记）</summary>
        public const string Prefix = "dpapi:";

        /// <summary>加密 → "dpapi:<base64>"。空输入原样返回（空密钥不包前缀）。</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return plain ?? "";
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null,
                DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(enc);
        }

        /// <summary>解密：dpapi: 前缀 → DPAPI Unprotect（失败返回 ""，见头注）；
        /// 无前缀 → 原样返回（明文兼容）。null 安全。</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored ?? "";
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
            try
            {
                byte[] plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored.Substring(Prefix.Length)), null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception)
            {
                // 换机/换账户/密文损坏：可显示原因的场景太罕见，统一降级为
                // 空密钥（HasBaiduKeys=false → 自动回 MyMemory），用户重存即恢复
                return "";
            }
        }

        /// <summary>值是否为 DPAPI 密文（诊断/测试用）</summary>
        public static bool IsProtected(string stored)
        {
            return stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}
