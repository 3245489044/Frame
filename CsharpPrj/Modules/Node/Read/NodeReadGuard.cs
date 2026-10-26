using Modules.Node.Identity;
using Modules.Node.Projection;

namespace Modules.Node.Read;

/// <summary>
/// 限定判定入参（FW-2 单源）：容器/轮次为**读限定**坐标，键集为已归一显式键。
/// </summary>
/// <param name="Domain">读域键。</param>
/// <param name="Container">期望容器（null = 不按容器裁剪）。</param>
/// <param name="Round">期望轮次（null = 不按轮次裁剪）。</param>
/// <param name="RequestedKeyCount">显式键数（&gt;0 表示有界读）。</param>
/// <param name="ExplicitFull">是否显式声明全量（<see cref="NodeKeySetScope.Full"/>）。</param>
public readonly record struct ReadGuardScope(
    string Domain,
    string? Container,
    int? Round,
    int RequestedKeyCount,
    bool ExplicitFull);

/// <summary>
/// 限定判定结果：放行项 + **注册枚举**原因 + 样本（FW-2/FW-6）。
/// </summary>
public sealed record NodeReadGuardResult(
    IReadOnlyList<NodeProjectionItem> Items,
    IReadOnlyList<NodeReadReason> ReasonCodes,
    IReadOnlyList<string> Samples)
{
    public static readonly NodeReadGuardResult Empty =
        new(Array.Empty<NodeProjectionItem>(), Array.Empty<NodeReadReason>(), Array.Empty<string>());
}

/// <summary>
/// 读**限定唯一判定**（FW-2）：容器/轮次裁剪 + 键族隔离，二者共用本实现。
/// <para>
/// 纪律：不可判**不得静默准入**——<see cref="ReadGuardScope"/> 缺坐标且无显式键时由
/// <see cref="NodeReadService"/> 直接返回 <see cref="NodeReadStatus.Unavailable"/>，
/// 本类只负责「已进入投影的项」逐项判定（不冒充全域）。
/// </para>
/// </summary>
public sealed class NodeReadGuard
{
    private const int SampleLimit = 5;

    private readonly Func<string, NodeFramework?> _frameworks;

    /// <param name="frameworks">域键 → 域框架（位置模板来源）；未装配 ⇒ <c>null</c>（不改判定）。</param>
    public NodeReadGuard(Func<string, NodeFramework?> frameworks)
        => _frameworks = frameworks ?? throw new ArgumentNullException(nameof(frameworks));

    /// <summary>逐项判定：返回放行项与裁剪原因（原因 ⊆ <see cref="NodeReadReason"/>）。</summary>
    public NodeReadGuardResult Admit(ReadGuardScope scope, IReadOnlyList<NodeProjectionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var framework = _frameworks(scope.Domain);
        if (framework is null)
        {
            return new NodeReadGuardResult(items, Array.Empty<NodeReadReason>(), Array.Empty<string>());
        }

        var kept = new List<NodeProjectionItem>(items.Count);
        var reasons = new List<NodeReadReason>();
        var samples = new List<string>(SampleLimit);

        foreach (var item in items)
        {
            if (!framework.Placements.TryGet(item.PlacementId, out var placement))
            {
                Reject(NodeReadReason.PlacementUnresolved, item.Key);
                continue;
            }

            var prefix = PlacementKeyPrefix.LiteralPrefixOf(placement.KeyTemplate, scope.Container, scope.Round);
            if (prefix is { Length: > 0 } &&
                !item.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                Reject(NodeReadReason.ForeignNamespace, item.Key);
                continue;
            }

            if (!NodeIdentityTrim.TryMatch(framework, item, scope.Container, scope.Round, out var reason))
            {
                Reject(MapTrimReason(reason), item.Key);
                continue;
            }

            kept.Add(item);
        }

        return new NodeReadGuardResult(kept, reasons, samples);

        void Reject(NodeReadReason reason, string key)
        {
            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }

            if (samples.Count < SampleLimit)
            {
                samples.Add($"{NodeReadReasons.ToToken(reason)}:{key}");
            }
        }
    }

    private static NodeReadReason MapTrimReason(string reason) => reason switch
    {
        "container-mismatch" => NodeReadReason.ContainerMismatch,
        "round-mismatch" => NodeReadReason.RoundMismatch,
        _ => NodeReadReason.PlacementUnresolved,
    };
}
