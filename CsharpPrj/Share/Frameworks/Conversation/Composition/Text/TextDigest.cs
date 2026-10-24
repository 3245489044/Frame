#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace Share.Composition.Text;

/// <summary>
/// 文本摘要唯一入口（SHA256 十六进制）—— 段哈希、计划指纹、装配指纹、前缀指纹共用本算法。
/// 通用层持有摘要权威，领域侧（快照/校验）委托至此，避免第二份摘要实现。
/// </summary>
public static class TextDigest
{
    /// <summary>文本 SHA256（null → 空串的哈希，确定性）。</summary>
    public static string Hash(string? text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));

    /// <summary>按行聚合摘要（有序；空序列 → 空串哈希）。</summary>
    public static string HashLines(IEnumerable<string?>? lines)
    {
        if (lines is null) return Hash(string.Empty);
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line ?? string.Empty);
        }
        return Hash(sb.ToString());
    }
}
