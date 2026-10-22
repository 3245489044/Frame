namespace Modules.Node.Identity;

/// <summary>
/// 节点地址（N1/N2）：逻辑身份 <c>(DomainKey, NodeKey)</c> + 实例轴 <see cref="InstanceKey"/> +
/// 容器归属 <see cref="ContainerKey"/>。
/// <para>
/// 存储键与图端点一律由本类型派生（<see cref="StorageKey"/> / <see cref="EndpointId"/>），
/// 禁调用点手拼 <c>@</c> 或分隔符；词法/正反解单源 = <see cref="NodeEndpointIds"/>（键文法内核）。
/// </para>
/// <para>
/// 与宿主 <c>NodeInstanceRef</c> 的差别：本类型只表达地址事实，不含落库/跳过原因等领域语义；
/// 后者降为适配器（H1 期接线）。
/// </para>
/// </summary>
/// <param name="ContainerKey">容器键（可空；不参与存储键与端点键）。</param>
/// <param name="DomainKey">域键（端点命名空间）。</param>
/// <param name="NodeKey">逻辑节点键（同域内唯一）。</param>
/// <param name="InstanceKey">实例键（可空 = legacy 裸键/逻辑地址）。</param>
public sealed record NodeAddress(
    string? ContainerKey,
    string DomainKey,
    string NodeKey,
    string? InstanceKey)
{
    /// <summary>构造地址（<paramref name="nodeKey"/> 空 ⇒ 抛：Snapshot 地址必须含逻辑节点键）。</summary>
    public static NodeAddress Create(
        string? containerKey, string? domainKey, string? nodeKey, string? instanceKey = null)
    {
        var node = nodeKey?.Trim() ?? string.Empty;
        if (node.Length == 0)
        {
            throw new ArgumentException("节点键为空：Snapshot 地址必须含逻辑节点键", nameof(nodeKey));
        }

        return new NodeAddress(Trim(containerKey), Trim(domainKey), node, BlankToNull(instanceKey));
    }

    /// <summary>legacy 形态（无实例、无容器）：只读回退用。</summary>
    public static NodeAddress FromLegacy(string? domainKey, string? nodeKey)
        => new(null, Trim(domainKey), nodeKey?.Trim() ?? string.Empty, null);

    /// <summary>是否有实例身份（false = legacy 裸键/逻辑地址）。</summary>
    public bool HasInstance => !string.IsNullOrEmpty(InstanceKey);

    /// <summary>存储主键：<c>{NodeKey}[@{InstanceKey}]</c>（文法单源 = <see cref="NodeEndpointIds"/>）。</summary>
    public string StorageKey => NodeEndpointIds.Storage(NodeKey, InstanceKey);

    /// <summary>图端点 Id：<c>{DomainKey}.{StorageKey}</c>（域为空 ⇒ 存储键原样）。</summary>
    public string EndpointId => NodeEndpointIds.Endpoint(DomainKey, NodeKey, InstanceKey);

    /// <summary>存储键合成（唯一构造点；禁调用点手拼分隔符）。</summary>
    public static string StorageKeyOf(string? nodeKey, string? instanceKey)
        => NodeEndpointIds.Storage(nodeKey, instanceKey);

    /// <summary>剥离实例后缀（legacy 兼容读）：非实例键 ⇒ 原样返回。</summary>
    public static string StripInstance(string? storageKey)
        => NodeEndpointIds.TryParseStorage(storageKey, out var nodeKey, out _) ? nodeKey : storageKey ?? string.Empty;

    /// <summary>反解端点 Id（<c>{domain}.{storageKey}</c>；失败 ⇒ false，不猜）。</summary>
    public static bool TryParse(string? endpointId, out NodeAddress address)
    {
        address = null!;
        if (!NodeEndpointIds.TryParseEndpoint(endpointId, out var domainKey, out var nodeKey, out var instanceKey))
        {
            return false;
        }

        address = new NodeAddress(null, domainKey, nodeKey, BlankToNull(instanceKey));
        return true;
    }

    /// <summary>反解存储键（可带域/容器归属；域与容器由调用方按读域给出）。</summary>
    public static bool TryParseStorage(
        string? storageKey, string? domainKey, string? containerKey, out NodeAddress address)
    {
        address = null!;
        if (!NodeEndpointIds.TryParseStorage(storageKey, out var nodeKey, out var instanceKey))
        {
            return false;
        }

        address = new NodeAddress(Trim(containerKey), Trim(domainKey), nodeKey, BlankToNull(instanceKey));
        return nodeKey.Length > 0;
    }

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;

    private static string? BlankToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
