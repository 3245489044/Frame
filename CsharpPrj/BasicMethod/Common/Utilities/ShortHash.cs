using System;
using System.Security.Cryptography;
using System.Text;

namespace Modules.Common.Utilities
{
    /// <summary>
    /// 通用稳定短哈希（纯函数）：SHA256 → 可选长度的小写 hex。
    /// 由输入文本派生稳定的短标识，与输入保持确定性；领域前缀/长度由调用方组合（如 "t_" + 前 8 位）。
    /// Agent.Core 的 AgentRunner.HashInput 即委托本工具（对齐单源实现原则）。
    /// </summary>
    public static class ShortHash
    {
        /// <summary>SHA256 取前 length 位 hex（length&lt;=0 取全量）；lowercase 控制大小写。</summary>
        public static string Sha256Hex(string input, int length = 16, bool lowercase = true)
        {
            var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
            if (length > 0 && length < hex.Length) hex = hex[..length];
            return lowercase ? hex.ToLowerInvariant() : hex;
        }
    }
}
