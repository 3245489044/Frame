using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Modules.Declaration;

/// <summary>
/// 声明源指纹（R3 判据载体 = 内容事实，非 mtime/执行者叙述）：
/// 逐源按名称序拼接 <c>name \0 content \1</c> 后取 SHA256。
/// </summary>
public static class DeclarationFingerprint
{
    public static string Compute(IEnumerable<KeyValuePair<string, string>> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var ordered = new List<KeyValuePair<string, string>>(sources);
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        var builder = new StringBuilder();
        foreach (var pair in ordered)
        {
            builder.Append(pair.Key).Append('\u0000').Append(pair.Value).Append('\u0001');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
