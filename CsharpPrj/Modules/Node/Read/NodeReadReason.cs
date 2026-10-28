namespace Modules.Node.Read;

/// <summary>
/// 读状态三态（FW-6 / I4）：显式给出，**禁由原因反推**。
/// <para><c>Unavailable</c> 是唯一允许消费方回退/清面的态；<c>OkButEmpty</c> 不得触发清面。</para>
/// </summary>
public enum NodeReadStatus
{
    Ok,
    OkButEmpty,
    Unavailable,
}

/// <summary>
/// 读原因**注册枚举**（FW-6 / I5）：原因词单源的类型化形态。
/// <para>
/// 文本词表仍是 <see cref="NodeKeySetReasons"/>（不删常量，零破坏）；本枚举是其类型化映射，
/// 消费方判据 = <c>ReasonCodes ⊆ NodeReadReason</c>，禁止裸字符串比较。
/// </para>
/// </summary>
public enum NodeReadReason
{
    /// <summary>键属于本域（未裁剪）。</summary>
    Admitted,

    /// <summary>入参键为空。</summary>
    EmptyKey,

    /// <summary>外来族（history./session. 等）。</summary>
    ForeignNamespace,

    /// <summary>端点形态不可反解。</summary>
    UnparsableKey,

    /// <summary>无归属边（合法空）。</summary>
    NoBinding,

    /// <summary>端口未装配 / 图不可用。</summary>
    SourceUnavailable,

    /// <summary>显式键集读时缺行。</summary>
    KeyMissing,

    /// <summary>边目标节点行缺失。</summary>
    EdgeTargetMissing,

    /// <summary>作用域请求缺坐标（Scoped 且无键、无容器、无前缀）⇒ Unavailable。</summary>
    ScopeUndefined,

    /// <summary>位置/键不可解析（裁剪）。</summary>
    PlacementUnresolved,

    /// <summary>容器不匹配（裁剪）。</summary>
    ContainerMismatch,

    /// <summary>轮次不匹配（裁剪）。</summary>
    RoundMismatch,

    /// <summary>塑形不合格（无标题/摘要/键）。</summary>
    Unshaped,

    /// <summary>坐标已给但读投影无行（与 <see cref="NoBinding"/> 区分：空必须可归因）。</summary>
    NoProjectionRows,
}

/// <summary>原因词文本 ↔ 注册枚举的唯一映射（禁各处 switch）。</summary>
public static class NodeReadReasons
{
    private static readonly IReadOnlyDictionary<string, NodeReadReason> ByToken =
        new Dictionary<string, NodeReadReason>(StringComparer.Ordinal)
        {
            [NodeKeySetReasons.NoBinding] = NodeReadReason.NoBinding,
            [NodeKeySetReasons.SourceUnavailable] = NodeReadReason.SourceUnavailable,
            [NodeKeySetReasons.ForeignNamespace] = NodeReadReason.ForeignNamespace,
            [NodeKeySetReasons.UnparsableKey] = NodeReadReason.UnparsableKey,
            [NodeKeySetReasons.EmptyKey] = NodeReadReason.EmptyKey,
            [NodeKeySetReasons.EdgeTargetMissing] = NodeReadReason.EdgeTargetMissing,
            [NodeKeySetReasons.KeyMissing] = NodeReadReason.KeyMissing,
            [NodeKeySetReasons.NoProjectionRows] = NodeReadReason.NoProjectionRows,
            ["scope-undefined"] = NodeReadReason.ScopeUndefined,
            ["placement-unresolved"] = NodeReadReason.PlacementUnresolved,
            ["container-mismatch"] = NodeReadReason.ContainerMismatch,
            ["round-mismatch"] = NodeReadReason.RoundMismatch,
            ["unshaped"] = NodeReadReason.Unshaped,
            ["admitted"] = NodeReadReason.Admitted,
        };

    /// <summary>样本文本（<c>reason:key</c>）取前缀归类；未注册 ⇒ <c>false</c>（调用方须显式处理，禁静默吞）。</summary>
    public static bool TryParse(string? text, out NodeReadReason reason)
    {
        reason = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var token = text.Trim();
        var colon = token.IndexOf(':');
        if (colon > 0)
        {
            token = token[..colon];
        }

        return ByToken.TryGetValue(token, out reason);
    }

    /// <summary>文本 → 枚举（未注册归 <see cref="NodeReadReason.PlacementUnresolved"/> 之外的兜底不伪造：返回 <c>false</c>）。</summary>
    public static bool TryMap(IEnumerable<string>? texts, out IReadOnlyList<NodeReadReason> reasons)
    {
        var mapped = new List<NodeReadReason>();
        var seen = new HashSet<NodeReadReason>();
        if (texts is not null)
        {
            foreach (var text in texts)
            {
                if (!TryParse(text, out var reason))
                {
                    continue;
                }

                if (seen.Add(reason))
                {
                    mapped.Add(reason);
                }
            }
        }

        reasons = mapped;
        foreach (var text in texts ?? Array.Empty<string>())
        {
            if (!TryParse(text, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>枚举 → 注册词（日志/门禁样例用）。</summary>
    public static string ToToken(NodeReadReason reason) => reason switch
    {
        NodeReadReason.Admitted => "admitted",
        NodeReadReason.EmptyKey => NodeKeySetReasons.EmptyKey,
        NodeReadReason.ForeignNamespace => NodeKeySetReasons.ForeignNamespace,
        NodeReadReason.UnparsableKey => NodeKeySetReasons.UnparsableKey,
        NodeReadReason.NoBinding => NodeKeySetReasons.NoBinding,
        NodeReadReason.SourceUnavailable => NodeKeySetReasons.SourceUnavailable,
        NodeReadReason.KeyMissing => NodeKeySetReasons.KeyMissing,
        NodeReadReason.NoProjectionRows => NodeKeySetReasons.NoProjectionRows,
        NodeReadReason.EdgeTargetMissing => NodeKeySetReasons.EdgeTargetMissing,
        NodeReadReason.ScopeUndefined => "scope-undefined",
        NodeReadReason.PlacementUnresolved => "placement-unresolved",
        NodeReadReason.ContainerMismatch => "container-mismatch",
        NodeReadReason.RoundMismatch => "round-mismatch",
        NodeReadReason.Unshaped => "unshaped",
        _ => "unknown",
    };
}
