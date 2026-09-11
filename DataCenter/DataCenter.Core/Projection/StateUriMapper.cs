using DataCenter.Abstractions.Cells;
using DataCenter.Protocol.Projection;

namespace Operation.StateBlock.Projection;

/// <summary>
/// 全键 ↔ 图 URI 的<b>唯一映射点</b>（DF7/R4：禁止各域自行拼接 URI 或手工剥离前缀）。
/// <list type="bullet">
/// <item>分层全键（写者/存储用）：<c>{namespace}:{scope}:{key}</c>，如 <c>{domain}.Session:taskA:sample.key</c></item>
/// <item>图 URI（导出/跨语言用）：<c>csg://{domain}/{tier}/{scope}/{key}</c>，如 <c>csg://{domain}/session/taskA/sample.key</c></item>
/// </list>
/// 展开规则：namespace 的 <c>.{Tier}</c> 后缀（由 <c>StateLayerScheme.ForDomain</c> 生成）拆为 URI 的 domain 与 tier 两段。
/// 域命名空间取值由接入方决定（本层不含任何具体域常量）。
/// </summary>
public static class StateUriMapper
{
    /// <summary>URI 方案名（对齐 CSG CanonicalForm.UriScheme）。</summary>
    public const string UriScheme = "csg";

    /// <summary>合成 scope 节点键（承载"分区归属"结构边的锚点；nodeKind = state.scope）。</summary>
    public const string ScopeNodeKey = "__scope";

    private const string UriPrefix = "csg://";

    /// <summary>分层标识的线上表示（URI 段/注册键）；默认集 config/session/transient 仅为示例。</summary>
    public static string TierId(DataTier tier) => tier switch
    {
        DataTier.Config => "config",
        DataTier.Session => "session",
        DataTier.Transient => "transient",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, $"不支持的分层标识: {tier}"),
    };

    /// <summary>解析状态条目为图坐标（domain / tier / scope / key + 节点 URI）。</summary>
    public static StateUriRef Resolve(StateEntry entry)
    {
        var domain = DomainOf(entry.Namespace, entry.Tier);
        var tierId = TierId(entry.Tier);
        var uri = $"{UriPrefix}{domain}/{tierId}/{entry.Scope}/{entry.Key}";
        return new StateUriRef(domain, tierId, entry.Scope, entry.Key, entry.Namespace, uri);
    }

    /// <summary>由状态条目直接取节点 URI（<see cref="Resolve"/> 的便捷形态）。</summary>
    public static string ToUri(StateEntry entry) => Resolve(entry).Uri;

    /// <summary>分区（scope）锚点节点 URI —— 结构边 <c>structure.owns</c> 的源端。</summary>
    public static string ScopeNodeUri(StateUriRef reference)
        => $"{UriPrefix}{reference.Domain}/{reference.TierId}/{reference.Scope}/{ScopeNodeKey}";

    /// <summary>拆解分层全键（<c>{namespace}:{scope}:{key}</c>）；不符合分层形态（如对象 KV 前缀键）返回 false。</summary>
    public static bool TrySplitFullKey(string fullKey, out string ns, out string scope, out string key)
    {
        ns = scope = key = string.Empty;
        if (string.IsNullOrWhiteSpace(fullKey)) return false;

        var first = fullKey.IndexOf(':');
        if (first <= 0) return false;

        var second = fullKey.IndexOf(':', first + 1);
        if (second <= first + 1) return false;

        ns = fullKey[..first];
        scope = fullKey[(first + 1)..second];
        key = fullKey[(second + 1)..];
        return key.Length > 0;
    }

    /// <summary>解析图 URI 为四段（domain/tier/scope/key）；key 允许含 '/'（取剩余全部）。</summary>
    public static bool TryParseUri(string uri, out StateUriParts parts)
    {
        parts = default;
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri[UriPrefix.Length..].Split('/');
        if (segments.Length < 4) return false;

        var key = string.Join('/', segments[3..]);
        if (segments[0].Length == 0 || segments[1].Length == 0 || segments[2].Length == 0 || key.Length == 0)
            return false;

        parts = new StateUriParts(segments[0], segments[1], segments[2], key);
        return true;
    }

    private static string DomainOf(string ns, DataTier tier)
    {
        var suffix = "." + tier.ToString();
        return ns.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? ns[..^suffix.Length]
            : ns;
    }
}

/// <summary>图坐标（domain/tier/scope/key + 节点 URI + 来源命名空间）。</summary>
public readonly record struct StateUriRef(
    string Domain,
    string TierId,
    string Scope,
    string Key,
    string Namespace,
    string Uri);

/// <summary>图 URI 的四段解析结果。</summary>
public readonly record struct StateUriParts(string Domain, string Tier, string Scope, string Key);
