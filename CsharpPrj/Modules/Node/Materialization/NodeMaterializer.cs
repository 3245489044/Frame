using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Identity;
using Modules.Node.Placement;

namespace Modules.Node.Materialization;

public enum MaterializeStatus
{
    Written,
    Unchanged,
    Conflict,
    Unbound,
    Rejected,
}

public sealed record NodeMaterializeRequest(
    string Key,
    string Json,
    string? Domain = null,
    IReadOnlyList<string>? PlacementIds = null,
    string? WriterId = null);

public sealed record NodePlacementResult(
    string PlacementId,
    string PlacementKey,
    CarrierWriteStatus? Status,
    string? Reason);

public sealed record NodeMaterializationResult(
    MaterializeStatus Status,
    string Key,
    IReadOnlyList<NodePlacementResult> Placements,
    IReadOnlyList<NodeDiagnostic> Diagnostics)
{
    public bool AnyWritten => Placements.Any(p => p.Status == CarrierWriteStatus.Written);
}

/// <summary>
/// <b>唯一写入口</b>（框架内所有落库经此；旁路调用点 = 0，治 P16）：
/// 身份键 → 位置集合 → 形态编码 → 单写门 → 载体 upsert（幂等 / 冲突可见化）。
/// 不做：决定"写哪些位置"之外的语义（位置由声明/反解给定）；不直连库（只经 <see cref="INodeCarrier"/>）。
/// </summary>
public sealed class NodeMaterializer
{
    private readonly IdentitySpec _identity;
    private readonly PlacementRegistry _placements;
    private readonly ICarrierRegistry _carriers;
    private readonly NodeWriteGate _writeGate;
    private readonly NodeFormCodec _codec;
    private readonly SpecDefaults _defaults;
    private readonly INodeDiagnosticsSink _diagnostics;
    private readonly INodeRunScopeSource _runScope;
    private readonly bool _requireRunScope;
    private readonly string? _domain;

    public NodeMaterializer(
        IdentitySpec identity,
        PlacementRegistry placements,
        ICarrierRegistry carriers,
        NodeWriteGate writeGate,
        NodeFormCodec? codec = null,
        SpecDefaults? defaults = null,
        INodeDiagnosticsSink? diagnostics = null,
        INodeRunScopeSource? runScopeSource = null,
        bool requireRunScope = false,
        string? domain = null)
    {
        _identity = identity;
        _placements = placements;
        _carriers = carriers;
        _writeGate = writeGate;
        _codec = codec ?? new NodeFormCodec();
        _defaults = defaults ?? new SpecDefaults();
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
        _runScope = runScopeSource ?? NullNodeRunScopeSource.Instance;
        _requireRunScope = requireRunScope;
        _domain = domain;
    }

    public NodeFormCodec Codec => _codec;

    /// <summary>
    /// 运行域校验（有供体且域命中才生效）：比对身份键 Context 段与当前 run ——
    /// ① 容器须与供体按该容器返回的运行域一致（供体实现护栏，防跨 run 串味）；
    /// ② 轮次**只拒超前**（round &gt; 当前轮）：旧轮积分合法（运行态面跨轮只增不丢，结算按条目自身轮次物化），
    ///    若逐字比对轮次，跨轮待落库项将永久被拒 ⇒ 静默丢数据。
    /// 无运行域 ⇒ 仅在 <c>requireRunScope</c> 开启时拒写（默认不拒，行为与既有一致）。
    /// </summary>
    private NodeRunScopeViolation? RunScopeViolation(
        NodeMaterializeRequest request, IReadOnlyDictionary<string, string> values)
    {
        // 域来源：框架自身域（按声明装配）优先，退回写请求显式域；两者皆无 ⇒ 不由运行域裁决。
        var domain = _domain ?? request.Domain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        values.TryGetValue("container", out var container);
        var scope = string.IsNullOrWhiteSpace(container) ? null : _runScope.Current(domain!, container!);
        if (scope is null)
        {
            return _requireRunScope
                ? new NodeRunScopeViolation(
                    "materialize.run-scope-missing",
                    $"域 '{domain}' 要求运行域但容器 '{container ?? "<无 container 段>"}' 无当前 run（未 BeginRun）")
                : null;
        }

        if (!string.Equals(scope.DomainKey, domain, StringComparison.Ordinal))
        {
            return null; // 供体给出的域与请求域不符：非本域运行态，交回既有流程裁决
        }

        if (!string.Equals(scope.Container, container, StringComparison.Ordinal))
        {
            // 供体实现护栏：本域供体须按「取值的容器」返回该容器的运行域（返回他容器 ⇒ 装配缺陷，不静默）。
            return new NodeRunScopeViolation(
                "materialize.run-scope-container-mismatch",
                $"身份键容器 '{container}' 与当前运行域 '{scope.Container}' 不一致（跨 run 串味）");
        }

        if (values.TryGetValue("round", out var roundText) &&
            int.TryParse(roundText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var round) &&
            round > scope.Round)
        {
            // 只拒「不可能的将来轮次」（身份漂移/伪造）；**旧轮积压合法**：
            // 运行态面跨轮只增不丢（BeginRound 保留未落库条目，结算按条目自身轮次物化），
            // 故 round < scope.Round 必须放行，否则跨轮待落库项永久被拒 ⇒ 静默丢数据。
            return new NodeRunScopeViolation(
                "materialize.run-scope-round-ahead",
                $"身份键轮次 '{round}' 超前于当前运行域轮次 '{scope.Round}'");
        }

        return null;
    }

    /// <summary>运行域违规（代码 + 可见化原因；<c>null</c> = 通过）。</summary>
    private readonly record struct NodeRunScopeViolation(string Code, string Message);

    public async Task<NodeMaterializationResult> MaterializeAsync(
        NodeMaterializeRequest request,
        CancellationToken cancellationToken = default)
    {
        var collected = new List<NodeDiagnostic>();
        var results = new List<NodePlacementResult>();

        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return Reject(request.Key, "materialize.empty-key", "身份键为空", collected, results);
        }

        if (!KeyTemplateEvaluator.TryParseValues(_identity.Template, request.Key, out var values))
        {
            return Reject(
                request.Key,
                "materialize.key-unparsable",
                $"身份键不符合身份规范 '{_identity.Template}'",
                collected,
                results);
        }

        // 运行域校验（可选）：身份键 Context 段须与框架侧「当前 run」一致 ——
        // 把「身份靠调用方自觉」升级为「框架可校验」；不一致即 Rejected（不写入任何位置）。
        if (RunScopeViolation(request, values) is { } violation)
        {
            return Reject(request.Key, violation.Code, violation.Message, collected, results);
        }

        var candidates = ResolvePlacements(request, collected);
        if (candidates.Count == 0)
        {
            return Reject(
                request.Key,
                "placement.none",
                "无可用位置声明（域内无 Active 位置 / 未指定位置）",
                collected,
                results);
        }

        // 缺省补齐只作用于"规范化载荷"：raw 位置按 N11 原样整存调用方原文，故惰性计算（缺省为空 ⇒ 原样）
        var mergedPayload = (string?)null;

        string PayloadSourceFor(PlacementSpec spec)
        {
            if (spec.Form != PlacementForm.Raw)
            {
                return mergedPayload ??= _defaults.Merge(request.Json);
            }

            if (_defaults.FieldCount > 0)
            {
                collected.Add(new NodeDiagnostic(
                    "form.raw-verbatim",
                    NodeDiagnosticSeverity.Info,
                    "形态 raw：按外部原文整存，缺省声明不注入（缺省仅作用于规范化载荷）",
                    spec.PlacementId));
            }

            return request.Json;
        }

        foreach (var spec in candidates)
        {
            if (spec.State == PlacementState.Retired)
            {
                collected.Add(new NodeDiagnostic(
                    "placement.retired",
                    NodeDiagnosticSeverity.Info,
                    "位置已退役：不再命中（跳过）",
                    spec.PlacementId));
                continue;
            }

            if (!_carriers.TryGet(spec.CarrierId, out var carrier))
            {
                collected.Add(new NodeDiagnostic(
                    "carrier.unbound",
                    NodeDiagnosticSeverity.Warning,
                    $"载体 '{spec.CarrierId}' 未注册：该位置记为 Unbound（不阻断其它位置）",
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, string.Empty, null, "carrier-unbound"));
                continue;
            }

            if (!spec.IsWritable)
            {
                collected.Add(new NodeDiagnostic(
                    "placement.legacy-read-only",
                    NodeDiagnosticSeverity.Error,
                    "位置为 Legacy：拒写可读",
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, string.Empty, CarrierWriteStatus.Rejected, "legacy-read-only"));
                continue;
            }

            if (request.WriterId is not null &&
                !string.Equals(request.WriterId, spec.WriterId, StringComparison.Ordinal))
            {
                collected.Add(new NodeDiagnostic(
                    "write.writer-mismatch",
                    NodeDiagnosticSeverity.Error,
                    $"写入者与位置声明不一致（声明 writer='{spec.WriterId}'）",
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, string.Empty, CarrierWriteStatus.Rejected, "writer-mismatch"));
                continue;
            }

            var placementKey = KeyTemplateEvaluator.Evaluate(spec.KeyTemplate, values);
            if (!_codec.IsLossless(spec.Form))
            {
                collected.Add(new NodeDiagnostic(
                    "form.lossy",
                    NodeDiagnosticSeverity.Info,
                    $"形态 '{spec.Form}' 有损（摘要不可还原原文，读侧须以原文位置为准）",
                    spec.PlacementId));
            }

            var payload = _codec.Encode(spec.Form, PayloadSourceFor(spec));
            var target = $"{spec.CarrierId}|{placementKey}";

            CarrierWriteResult write;
            try
            {
                write = await _writeGate
                    .RunAsync(target, () => carrier.UpsertAsync(placementKey, payload, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (NodeWriteConflictException ex)
            {
                collected.Add(new NodeDiagnostic(
                    "write.gate-conflict",
                    NodeDiagnosticSeverity.Error,
                    ex.Message,
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, placementKey, CarrierWriteStatus.Rejected, "gate-conflict"));
                continue;
            }

            if (write.Status == CarrierWriteStatus.Conflict)
            {
                collected.Add(new NodeDiagnostic(
                    "write.conflict",
                    NodeDiagnosticSeverity.Error,
                    $"同位置异载荷：{write.Reason}（版本化/可见化冲突，不静默覆盖）",
                    placementKey));
            }

            results.Add(new NodePlacementResult(spec.PlacementId, placementKey, write.Status, write.Reason));
        }

        Report(collected);

        return new NodeMaterializationResult(Aggregate(results), request.Key, results, collected);
    }

    private IReadOnlyList<PlacementSpec> ResolvePlacements(NodeMaterializeRequest request, List<NodeDiagnostic> collected)
    {
        if (request.PlacementIds is { Count: > 0 })
        {
            var selected = new List<PlacementSpec>();
            foreach (var id in request.PlacementIds)
            {
                if (_placements.TryGet(id, out var spec))
                {
                    selected.Add(spec);
                }
                else
                {
                    collected.Add(new NodeDiagnostic(
                        "placement.unknown",
                        NodeDiagnosticSeverity.Error,
                        $"位置未声明: {id}",
                        request.Key));
                }
            }

            return selected;
        }

        var domain = request.Domain ?? string.Empty;
        return domain.Length == 0
            ? Array.Empty<PlacementSpec>()
            : _placements.ByDomain(domain).ToArray();
    }

    private static MaterializeStatus Aggregate(IReadOnlyList<NodePlacementResult> results)
    {
        if (results.Any(r => r.Status == CarrierWriteStatus.Conflict))
        {
            return MaterializeStatus.Conflict;
        }

        if (results.Any(r => r.Status == CarrierWriteStatus.Rejected))
        {
            return MaterializeStatus.Rejected;
        }

        if (results.Any(r => r.Status is null))
        {
            return MaterializeStatus.Unbound;
        }

        return results.Any(r => r.Status == CarrierWriteStatus.Written)
            ? MaterializeStatus.Written
            : MaterializeStatus.Unchanged;
    }

    private NodeMaterializationResult Reject(
        string key,
        string code,
        string message,
        List<NodeDiagnostic> collected,
        List<NodePlacementResult> results)
    {
        collected.Add(new NodeDiagnostic(code, NodeDiagnosticSeverity.Error, message, key));
        Report(collected);
        return new NodeMaterializationResult(MaterializeStatus.Rejected, key, results, collected);
    }

    private void Report(List<NodeDiagnostic> collected)
    {
        foreach (var diagnostic in collected)
        {
            _diagnostics.Report(diagnostic);
        }
    }
}
