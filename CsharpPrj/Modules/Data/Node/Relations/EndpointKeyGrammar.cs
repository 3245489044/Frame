using System;

namespace Modules.Node.Relations;

/// <summary>
/// 端点键文法（Modules B-2 单源）：<c>{type}:{id}</c>，可选 <c>@{location}</c>。
/// <para>
/// 端点键被图读写两侧共用（边键前缀、级联清边、旧形态回退），属图内核稳定契约；
/// 存储层只允许通过本类构造/反解/剥位置，禁止各自手写分隔符字面量。
/// 旧形态 <c>type:id</c>（无 <c>@location</c>）解析时 Location 为 null。
/// </para>
/// </summary>
public static class EndpointKeyGrammar
{
    /// <summary>类型与 Id 分隔符。</summary>
    public const char TypeSeparator = ':';

    /// <summary>位置段分隔符。</summary>
    public const char LocationSeparator = '@';

    /// <summary>构造端点键：Location 为空/空白 → <c>{type}:{id}</c>；否则 <c>{type}:{id}@{location}</c>。</summary>
    public static string Compose(string resourceType, string id, string? location = null)
        => string.IsNullOrWhiteSpace(location)
            ? resourceType + TypeSeparator + id
            : resourceType + TypeSeparator + id + LocationSeparator + location;

    /// <summary>反解端点键（兼容无 <c>@location</c> 的旧形态）。</summary>
    public static bool TryParse(string? key, out string resourceType, out string id, out string? location)
    {
        resourceType = string.Empty;
        id = string.Empty;
        location = null;

        if (string.IsNullOrWhiteSpace(key)) return false;

        var typeEnd = key.IndexOf(TypeSeparator);
        if (typeEnd <= 0) return false;

        var typeText = key[..typeEnd];
        var rest = key[(typeEnd + 1)..];
        if (rest.Length == 0) return false;

        var at = rest.LastIndexOf(LocationSeparator);
        var idText = at < 0 ? rest : rest[..at];
        var locationText = at < 0 ? null : rest[(at + 1)..];
        if (idText.Length == 0) return false;
        if (at >= 0 && string.IsNullOrEmpty(locationText)) return false;

        resourceType = typeText;
        id = idText;
        location = locationText;
        return true;
    }

    /// <summary>剥离端点键的 <c>@location</c> 段（旧形态兼容读）；无 <c>@</c> → 原样返回。</summary>
    public static string StripLocation(string endpointKey)
    {
        if (string.IsNullOrEmpty(endpointKey)) return endpointKey;

        var at = endpointKey.LastIndexOf(LocationSeparator);
        return at < 0 ? endpointKey : endpointKey.Substring(0, at);
    }

    /// <summary>是否含 <c>@location</c> 的位置段。</summary>
    public static bool HasLocation(string? endpointKey)
        => !string.IsNullOrEmpty(endpointKey) && endpointKey.LastIndexOf(LocationSeparator) >= 0;
}
