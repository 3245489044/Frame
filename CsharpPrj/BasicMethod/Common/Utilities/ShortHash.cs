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
            => Sha256Hex(Encoding.UTF8.GetBytes(input ?? string.Empty), length, lowercase);

        /// <summary>
        /// SHA256 字节面（内容指纹唯一实现）：<paramref name="length"/>&lt;=0 取全量 hex。
        /// <para>口径单源：全量/前 N 位、大写/小写均由调用方显式声明，禁各处手抄 HashData+ToHexString。</para>
        /// </summary>
        public static string Sha256Hex(ReadOnlySpan<byte> bytes, int length = 0, bool lowercase = true)
        {
            var hex = Convert.ToHexString(SHA256.HashData(bytes));
            if (length > 0 && length < hex.Length) hex = hex[..length];
            return lowercase ? hex.ToLowerInvariant() : hex;
        }

        /// <summary>文本全量 SHA256 hex（大写；跨来源摘要比对用同一形态）。</summary>
        public static string Sha256HexFull(string? input)
            => Sha256Hex(Encoding.UTF8.GetBytes(input ?? string.Empty), 0, lowercase: false);
    }
}
