using System.Security.Cryptography;
using System.Text;

namespace Share.ContextGeometry;

/// <summary>
/// 内容指纹（SHA-256 小写十六进制）。
/// <para>与 <c>Share.Messaging</c> 各自实现（两侧均须零 ProjectReference），由对拍单测兜底（ADR D-3）。</para>
/// </summary>
public static class ContentHash
{
    /// <summary>文本 → SHA-256 小写十六进制（null 视为空串，不抛）。</summary>
    public static string Sha256Hex(string? text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).ToLowerInvariant();
}
