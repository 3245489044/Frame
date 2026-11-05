using System;
using System.Collections.Generic;
using System.Text;

namespace Share.PortContexts;

/// <summary>单成员分页请求（游标无状态；<see cref="Cursor"/> = null 表示首页）。</summary>
/// <param name="MemberKey">成员键（唯一允许的读取轴）。</param>
/// <param name="Cursor">游标（<see cref="PortContextCursor"/> 编码；null = 首页）。</param>
/// <param name="Take">期望条数（≤0 ⇒ 规格缺省；超上限由装配器夹取）。</param>
public sealed record PortContextPageRequest(string MemberKey, string? Cursor, int Take);

/// <summary>
/// 单成员一页窗口（条 = 结构化消息项）：<paramref name="HasMore"/> / <paramref name="NextCursor"/>
/// 由装配器给出；<paramref name="EmptyCode"/> 非空 ⇒ 空页且空因可见（禁静默空）。
/// </summary>
public sealed record PortContextPage(
    string ContainerKey,
    string MemberKey,
    IReadOnlyList<PortContextWindowItem> Items,
    string? NextCursor,
    bool HasMore,
    string? EmptyCode,
    bool Truncated)
{
    /// <summary>是否带空因。</summary>
    public bool IsEmpty => !string.IsNullOrWhiteSpace(EmptyCode);

    /// <summary>空页（空因必显式；禁静默空）。</summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="memberKey">成员键。</param>
    /// <param name="code">空因码（受控词）。</param>
    public static PortContextPage Empty(string containerKey, string memberKey, string code)
        => new(containerKey, memberKey, Array.Empty<PortContextWindowItem>(), null, false, code, false);
}

/// <summary>
/// 分页游标编解码（单一实现）：无状态 <c>base64url("成员键|离尾偏移")</c>，非法 ⇒ 解码失败
/// （由装配器落 <see cref="PortContextEmptyCodes.CursorInvalid"/>，禁静默回首页）。
/// </summary>
public static class PortContextCursor
{
    /// <summary>离尾偏移上界（有界：防无界读取与无界缓存键）。</summary>
    public const int MaxOffsetFromEnd = 192;

    private const string Separator = "|";

    /// <summary>编码（成员键 + 离尾偏移）。</summary>
    /// <param name="memberKey">成员键。</param>
    /// <param name="offsetFromEnd">已跳过的消息条数（≤0 ⇒ 首页）。</param>
    public static string Encode(string memberKey, int offsetFromEnd)
    {
        var offset = Math.Clamp(offsetFromEnd, 0, MaxOffsetFromEnd);
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(memberKey + Separator + offset));
        return raw.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>解码（<paramref name="cursor"/> 为空 ⇒ 首页偏移 0）。</summary>
    /// <param name="cursor">游标原文。</param>
    /// <param name="expectedMemberKey">调用方请求的成员键（不符 ⇒ 失败）。</param>
    /// <param name="offsetFromEnd">解出的离尾偏移。</param>
    public static bool TryDecode(string? cursor, string expectedMemberKey, out int offsetFromEnd)
    {
        offsetFromEnd = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;

        try
        {
            var normalized = cursor!.Trim().Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding == 2) normalized += "==";
            else if (padding == 3) normalized += "=";
            else if (padding == 1) return false;

            var text = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var index = text.LastIndexOf(Separator, StringComparison.Ordinal);
            if (index <= 0) return false;

            var member = text.Substring(0, index);
            if (!string.Equals(member, expectedMemberKey, StringComparison.Ordinal)) return false;
            if (!int.TryParse(text.AsSpan(index + 1), out var offset)) return false;
            if (offset < 0 || offset > MaxOffsetFromEnd) return false;

            offsetFromEnd = offset;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
