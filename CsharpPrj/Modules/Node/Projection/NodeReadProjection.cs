using System.Text.Json.Nodes;
using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Identity;
using Modules.Node.Materialization;
using Modules.Node.Placement;
using Modules.Node.Spec;

namespace Modules.Node.Projection;

/// <summary>运行态来源（装配优先级 Live &gt; Snapshot &gt; Derived，按来源优先级做<b>字段级</b>叠加）。</summary>
public enum ProjectionOrigin
{
    /// <summary>内存装配（如 LoopFoldMethod 的节点装配结果），不落库。</summary>
    Live,

    /// <summary>已物化（载体可读）。</summary>
    Snapshot,

    /// <summary>投影/聚合派生。</summary>
    Derived,
}

/// <summary>
/// 读投影项（**行身份三分离**：<c>Key</c> = 显示/定位键；<c>DomainKey</c> = 读域；
/// <c>(<see cref="WriteDomain"/>, <see cref="SourceKey"/>)</c> = 写/删/详读身份）。
/// </summary>
/// <param name="Key">显示键（呈现/定位用；可为合成键，如 <c>{表名}:{真键}</c>）。</param>
/// <param name="PlacementId">位置键。</param>
/// <param name="Fields">展平字段。</param>
/// <param name="Origin">来源优先级。</param>
/// <param name="PayloadJson">载荷 JSON（可空）。</param>
/// <param name="DomainKey">读域（行来源域；可空 = 回退模型域键）。</param>
/// <param name="SourceKey">真键（写/删/详读身份；可空 = 回退 <see cref="Key"/>）。</param>
/// <param name="WriteDomain">写域（路由用；null ⇒ 回退 <see cref="DomainKey"/>，即「节点域自写」）。</param>
/// <param name="Address">节点地址（Snapshot 完整化：逻辑键 + 实例轴；Live 可空 = 身份不完整）。</param>
/// <param name="Tags">标签集（<c>|</c> 规范串；null = 无标签）。</param>
/// <param name="Signal">信号元数据（排序/过滤/预算用；null = 未提供）。</param>
public sealed record NodeProjectionItem(
    string Key,
    string PlacementId,
    IReadOnlyDictionary<string, string> Fields,
    ProjectionOrigin Origin,
    string? PayloadJson = null,
    string? DomainKey = null,
    string? SourceKey = null,
    string? WriteDomain = null,
    NodeAddress? Address = null,
    string? Tags = null,
    NodeSignal? Signal = null);

/// <summary>
/// 读投影范围（裁剪只在读侧：N7）。
/// <para>
/// 位置来源（L-6）：<see cref="Loc"/> 给出时经 <see cref="IPlacementResolver"/> 反解 —— 与写侧**同一实现**
/// （消除"写侧按 A 位置、读侧按 B 位置"的漂移）；<see cref="PlacementIds"/> 保留为兼容/收窄面
/// （未给 <see cref="Loc"/> 时按现状直取注册表；给出 <see cref="Loc"/> 时作为**交集收窄**）。
/// </para>
/// </summary>
/// <remarks>
/// <c>Loc</c> = loc 值（位置轴坐标，非身份 L-1）；给出 ⇒ 位置集合 = 反解产物
/// （未声明/无命中 ⇒ ∅ + 诊断，禁回退默认载体 L-5）。
/// </remarks>
public sealed record ReadScope(
    string? Domain = null,
    IReadOnlyList<string>? PlacementIds = null,
    string? SelectionId = null,
    string? KeyPrefix = null,
    int? Limit = null,
    string? Loc = null,
    string? Container = null,
    int? Round = null,
    NodeSelectionSpec? Selection = null,
    IReadOnlyList<string>? Keys = null);

/// <summary>
/// 读投影：<c>Scope → placements → 解码 → selection → 排序 → Origin 标注</c>。
/// 顺序由键内序数决定（N6：删除全部边后顺序不变，枚举序 = 键序数序）；无来源命中 ⇒ ∅（不伪造空闲态）。
/// 不写库。
/// </summary>
public sealed class NodeReadProjection
{
    private readonly PlacementRegistry _placements;
    private readonly ICarrierRegistry _carriers;
    private readonly SelectionSet _selections;
    private readonly NodeFormCodec _codec;
    private readonly INodeDiagnosticsSink _diagnostics;
    private readonly IPlacementResolver? _placementResolver;

    /// <summary>词表端口（U3）：<c>all</c> 选择名由之提供；未装配 ⇒ 既有字面量（零行为变更）。</summary>
    private readonly ISelectionVocabulary _vocabulary;

    /// <remarks>
    /// <c>placementResolver</c> = 位置反解端口（L-6）：<c>ReadScope.Loc</c> 给出时读侧位置集合由此产出
    /// （与写侧同一实现）。未装配 ⇒ 按 loc 读时为 ∅ + 诊断（不兜底）；<c>null</c> 时 <c>Loc</c> 不可用。
    /// </remarks>
    public NodeReadProjection(
        PlacementRegistry placements,
        ICarrierRegistry carriers,
        SelectionSet selections,
        NodeFormCodec? codec = null,
        INodeDiagnosticsSink? diagnostics = null,
        IPlacementResolver? placementResolver = null,
        ISelectionVocabulary? vocabulary = null)
    {
        _placements = placements;
        _carriers = carriers;
        _selections = selections;
        _codec = codec ?? new NodeFormCodec();
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
        _placementResolver = placementResolver;
        _vocabulary = vocabulary ?? NullSelectionVocabulary.Instance;
    }

    public async Task<IReadOnlyList<NodeProjectionItem>> ProjectAsync(
        ReadScope scope,
        IReadOnlyList<NodeProjectionItem>? overlays = null,
        CancellationToken cancellationToken = default)
    {
        var targets = ResolvePlacements(scope);
        var items = new List<NodeProjectionItem>();

        if (scope.Keys is { Count: > 0 } explicitKeys)
        {
            if (!string.IsNullOrEmpty(scope.KeyPrefix))
            {
                throw new ArgumentException(
                    "ReadScope.Keys 与 KeyPrefix 互斥（禁二义读轴）", nameof(scope));
            }

            foreach (var placement in targets)
            {
                if (!_carriers.TryGet(placement.CarrierId, out var carrier))
                {
                    _diagnostics.Report(new NodeDiagnostic(
                        "carrier.unbound",
                        NodeDiagnosticSeverity.Warning,
                        $"载体 '{placement.CarrierId}' 未注册：该位置读投影为空（不阻断其它位置）",
                        placement.PlacementId));
                    continue;
                }

                foreach (var key in explicitKeys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var record = await carrier.ReadAsync(key, cancellationToken).ConfigureAwait(false);
                    if (record is null)
                    {
                        _diagnostics.Report(new NodeDiagnostic(
                            "projection.key-missing",
                            NodeDiagnosticSeverity.Warning,
                            $"显式键集读：键 '{key}' 在载体 '{placement.CarrierId}' 无行（不伪造）",
                            key));
                        continue;
                    }

                    var payload = _codec.Decode(placement.Form, record.Json);
                    NodeAddress.TryParseStorage(key, placement.Domain, scope.Container, out var address);
                    items.Add(new NodeProjectionItem(
                        key, placement.PlacementId, Flatten(payload), ProjectionOrigin.Snapshot, payload,
                        DomainKey: placement.Domain, Address: address, SourceKey: key));
                }
            }
        }
        else
        {
            foreach (var placement in targets)
            {
                if (!_carriers.TryGet(placement.CarrierId, out var carrier))
                {
                    _diagnostics.Report(new NodeDiagnostic(
                        "carrier.unbound",
                        NodeDiagnosticSeverity.Warning,
                        $"载体 '{placement.CarrierId}' 未注册：该位置读投影为空（不阻断其它位置）",
                        placement.PlacementId));
                    continue;
                }

                // 读前缀**逐位置**派生（位置键模板；显式 scope.KeyPrefix 优先）：
                // 单键身份模板（{container}）不能当前缀来源，否则载体全量枚举且跨域混读。
                var prefix = scope.KeyPrefix ?? PlacementKeyPrefix.LiteralPrefixOf(
                    placement.KeyTemplate, scope.Container, scope.Round);
                var keys = await carrier.EnumerateKeysAsync(prefix, cancellationToken).ConfigureAwait(false);

                foreach (var key in keys)
                {
                    if (prefix is { Length: > 0 } && !key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var record = await carrier.ReadAsync(key, cancellationToken).ConfigureAwait(false);
                    if (record is null)
                    {
                        continue;
                    }

                    var payload = _codec.Decode(placement.Form, record.Json);
                    // Snapshot 完整化：地址由存储键反解（实例轴随键保留；容器/域取读侧作用域）。
                    NodeAddress.TryParseStorage(key, placement.Domain, scope.Container, out var address);
                    items.Add(new NodeProjectionItem(
                        key, placement.PlacementId, Flatten(payload), ProjectionOrigin.Snapshot, payload,
                        DomainKey: placement.Domain, Address: address));
                }
            }
        }

        var merged = Overlay(items, overlays ?? Array.Empty<NodeProjectionItem>());
        var filtered = ApplySelection(merged, targets, scope);

        var ordered = filtered
            .OrderBy(item => item.Key, NodeKeyOrdinalComparer.Instance)
            .ThenBy(item => item.PlacementId, StringComparer.Ordinal)
            .ToArray();

        var limited = scope.Limit is { } limit && limit > 0 && ordered.Length > limit
            ? ordered.Take(limit).ToArray()
            : ordered;

        if (limited.Length == 0)
        {
            _diagnostics.Report(new NodeDiagnostic(
                "projection.empty",
                NodeDiagnosticSeverity.Info,
                "读投影无来源命中：返回 ∅（不伪造状态）",
                scope.Loc ?? scope.Domain ?? scope.PlacementIds?.FirstOrDefault() ?? scope.KeyPrefix ?? string.Empty));
        }

        return limited;
    }

    private IReadOnlyList<PlacementSpec> ResolvePlacements(ReadScope scope)
    {
        if (scope.Loc is { Length: > 0 } loc)
        {
            return ResolveByLoc(scope, loc);
        }

        if (scope.PlacementIds is { Count: > 0 })
        {
            var selected = new List<PlacementSpec>();
            foreach (var id in scope.PlacementIds)
            {
                if (_placements.TryGet(id, out var spec) && spec.IsReadable)
                {
                    selected.Add(spec);
                }
            }

            return selected;
        }

        return scope.Domain is { Length: > 0 } domain
            ? _placements.ReadableByDomain(domain)
            : _placements.All.Where(p => p.IsReadable).ToArray();
    }

    /// <summary>
    /// loc 反解取位置（L-6 单源）：与写侧共用同一 <see cref="IPlacementResolver"/> 实例。
    /// 失败即 ∅（诊断由反解端口给出，L-5 禁回退默认载体）；<see cref="ReadScope.Domain"/> /
    /// <see cref="ReadScope.PlacementIds"/> 仅作**收窄**（不改变位置来源）。
    /// </summary>
    private IReadOnlyList<PlacementSpec> ResolveByLoc(ReadScope scope, string loc)
    {
        if (_placementResolver is null)
        {
            _diagnostics.Report(new NodeDiagnostic(
                "projection.loc-without-resolver",
                NodeDiagnosticSeverity.Error,
                "按 loc 读位置但反解端口未装配：位置集合为空（不兜底）",
                loc));
            return Array.Empty<PlacementSpec>();
        }

        var resolution = _placementResolver.Resolve(loc, scope.KeyPrefix ?? string.Empty);
        if (!resolution.Resolved)
        {
            // L-5：未声明 loc / 反解无命中 ⇒ ∅ + 诊断（反解端口已可见化），禁回退默认载体
            return Array.Empty<PlacementSpec>();
        }

        var narrowing = scope.PlacementIds is { Count: > 0 }
            ? new HashSet<string>(scope.PlacementIds, StringComparer.Ordinal)
            : null;
        var domainFilter = scope.Domain is { Length: > 0 } domain ? domain : null;

        var targets = new List<PlacementSpec>(resolution.Placements.Count);
        foreach (var spec in resolution.Placements)
        {
            if (!spec.IsReadable)
            {
                continue;
            }

            if (domainFilter is not null && !string.Equals(spec.Domain, domainFilter, StringComparison.Ordinal))
            {
                continue;
            }

            if (narrowing is not null && !narrowing.Contains(spec.PlacementId))
            {
                continue;
            }

            targets.Add(spec);
        }

        return targets;
    }

    private IReadOnlyList<NodeProjectionItem> ApplySelection(
        IReadOnlyList<NodeProjectionItem> items,
        IReadOnlyList<PlacementSpec> targets,
        ReadScope scope)
    {
        var result = new List<NodeProjectionItem>();

        foreach (var item in items)
        {
            var selectionId = scope.SelectionId
                              ?? targets.FirstOrDefault(p => string.Equals(p.PlacementId, item.PlacementId, StringComparison.Ordinal))?.SelectionId
                              ?? _vocabulary.AllSelectionId;

            if (_selections.Accept(selectionId, item.Fields))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>按来源优先级做字段级叠加（低优先级填空，高优先级非空覆盖）。</summary>
    private static IReadOnlyList<NodeProjectionItem> Overlay(
        IReadOnlyList<NodeProjectionItem> snapshotItems,
        IReadOnlyList<NodeProjectionItem> overlays)
    {
        var order = new[] { ProjectionOrigin.Derived, ProjectionOrigin.Snapshot, ProjectionOrigin.Live };
        var buckets = new Dictionary<string, List<NodeProjectionItem>>(StringComparer.Ordinal);

        foreach (var item in snapshotItems.Concat(overlays))
        {
            var identity = item.Key + "|" + item.PlacementId;
            if (!buckets.TryGetValue(identity, out var list))
            {
                list = new List<NodeProjectionItem>();
                buckets[identity] = list;
            }

            list.Add(item);
        }

        var merged = new List<NodeProjectionItem>();

        foreach (var bucket in buckets.Values)
        {
            var ordered = bucket.OrderBy(item => Array.IndexOf(order, item.Origin)).ToArray();
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            string? payload = null;
            NodeAddress? address = null;
            NodeSignal? signal = null;
            var tags = new List<string>();
            var origin = ordered[0].Origin; // 初值仅为确定赋值；循环内必覆盖为最高优先级来源

            foreach (var item in ordered)
            {
                foreach (var pair in item.Fields)
                {
                    // 非空覆盖（字段级叠加语义）：高优先级来源的空值不抹掉低优先级非空值
                    if (string.IsNullOrEmpty(pair.Value) && fields.ContainsKey(pair.Key))
                    {
                        continue;
                    }

                    fields[pair.Key] = pair.Value;
                }

                if (!string.IsNullOrEmpty(item.PayloadJson))
                {
                    payload = item.PayloadJson;
                }

                // 结构化维度按「最高优先级非空者胜」叠加（与字段面同序：Derived → Snapshot → Live）
                if (item.Address is not null)
                {
                    address = item.Address;
                }

                if (item.Signal is not null)
                {
                    signal = item.Signal;
                }

                if (!string.IsNullOrWhiteSpace(item.Tags))
                {
                    foreach (var tag in item.Tags.Split(
                                 new[] { '|', ',', ';' },
                                 StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!tags.Contains(tag, StringComparer.Ordinal))
                        {
                            tags.Add(tag);
                        }
                    }
                }

                origin = item.Origin;
            }

            merged.Add(new NodeProjectionItem(
                ordered[0].Key, ordered[0].PlacementId, fields, origin, payload,
                ordered[0].DomainKey, ordered[0].SourceKey, ordered[0].WriteDomain,
                address, tags.Count == 0 ? null : string.Join("|", tags), signal));
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> Flatten(string payload)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        // 解析宽容度单源 = NodeJson（与写侧一致：尾逗号/注释跳过）
        if (!NodeJson.TryParse(payload, out var node, out _) || node is not JsonObject obj)
        {
            // 非 JSON 载荷：字段面为空（读侧不伪造结构）
            return fields;
        }

        foreach (var pair in obj)
        {
            if (pair.Value is null)
            {
                continue;
            }

            fields[pair.Key] = FlattenValue(pair.Value);
        }

        return fields;
    }

    /// <summary>
    /// 值 → 扁平文本（读侧字段面单源）：数组展开为 <c>|</c> 连接串（<b>集合谓词 <c>has</c>/<c>hasany</c> 的载体口径</b>），
    /// 其余复用 <see cref="NodeJson.ValueToText"/>（端口值语义不受影响——本归一仅作用于读投影字段面）。
    /// </summary>
    private static string FlattenValue(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonArray array => string.Join(
            "|",
            array.Where(element => element is not null).Select(element => FlattenValue(element))),
        _ => NodeJson.ValueToText(node),
    };
}
