#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 写回去重指纹原语（单一实现，E-6）——框架写协调器与领域渲染侧共用同一哈希口径，
/// 消除「框架 <c>_lastWrite</c> 全串比较」与「领域 StableFingerprint」两套（对齐 Strategy.SingleImplementation）。
/// <para>去重语义：同目标 + 同指纹 ⇒ 不触 DOM（映射 <c>UiWriteStatus.Nothing</c>）；指纹由调用方保证剔除易变字段（elapsed 等）。</para>
/// <para>纯函数（零状态、零领域语义）。</para>
/// </summary>
public static class UiFingerprint
{
    /// <summary>内容指纹（SHA-256 十六进制；null/空 → 定值，保证可比较）。</summary>
    public static string Of(string? s)
        => string.IsNullOrEmpty(s)
            ? "empty"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>多段合并指纹（按序拼接，段间用不可歧义分隔符，避免不同切分同串）。</summary>
    public static string Combine(params string?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts) sb.Append(p ?? string.Empty).Append('\u0001');
        return Of(sb.ToString());
    }
}
