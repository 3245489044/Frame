using System.Security.Cryptography;
using System.Text;

namespace Share.Messaging;

/// <summary>
/// 键/摘要规范（纯函数）。内核只给逻辑 MessageId 与稳定 token，物理存储键由适配器保留。
/// </summary>
public static class MessageCodec
{
    /// <summary>稳定 token 长度（SHA256 十六进制前缀）。</summary>
    public const int StableTokenLength = 12;

    /// <summary>内容摘要（SHA256 十六进制；空载荷给空串摘要，不抛）。</summary>
    public static string Digest(string? text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));

    /// <summary>稳定 token（同一逻辑 Id 恒得同一结果；供物理键 slot 复用）。</summary>
    public static string StableToken(string? messageId)
    {
        var digest = Digest(messageId);
        return digest.Length <= StableTokenLength ? digest : digest[..StableTokenLength];
    }

    /// <summary>构造逻辑消息 Id（<c>{scope}:{kind}:{guid}</c>；不含时间，禁缓存前缀污染）。</summary>
    public static string NewId(string scope, string kind)
        => $"{Normalize(scope, nameof(scope))}:{Normalize(kind, nameof(kind))}:{Guid.NewGuid():N}";

    /// <summary>规范化字段（空 ⇒ 显式异常，禁静默缺省）。</summary>
    public static string Normalize(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"消息字段 {name} 不得为空（禁静默缺省）。", name);
        return value.Trim();
    }
}
