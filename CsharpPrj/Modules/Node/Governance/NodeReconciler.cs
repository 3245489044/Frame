using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Identity;
using Modules.Node.Placement;
using Modules.Node.Spec;

namespace Modules.Node.Governance;

/// <summary>
/// 对账发现：<c>missing</c> 位置声明的载体未注册（声明面缺数据承载）/ <c>orphan</c> 数据缺声明 /
/// <c>drift</c> 键模板漂移 / <c>ambiguous</c> 多解。
/// </summary>
public sealed record ReconciliationFinding(string Kind, string Subject, string Detail);

public sealed record ReconciliationReport(IReadOnlyList<ReconciliationFinding> Findings)
{
    public bool IsClean => Findings.Count == 0;

    public IReadOnlyList<ReconciliationFinding> OfKind(string kind) =>
        Findings.Where(f => string.Equals(f.Kind, kind, StringComparison.Ordinal)).ToArray();
}

/// <summary>
/// 对账（S5）：按声明检缺失 / 孤儿 / 键模板漂移；<b>只报不改</b>（不擅自改数据，重建入口幂等）。
/// 位置面以构造时注入的 <see cref="PlacementRegistry"/> 为准（与装配同源），
/// <see cref="NodeFrameworkSpec"/> 仅提供身份面（漂移判定用），二者不混用（禁双源快照）。
/// </summary>
public sealed class NodeReconciler
{
    private readonly PlacementRegistry _placements;
    private readonly ICarrierRegistry _carriers;
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeReconciler(
        PlacementRegistry placements,
        ICarrierRegistry carriers,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _placements = placements;
        _carriers = carriers;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        NodeFrameworkSpec spec,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<ReconciliationFinding>();

        foreach (var placement in _placements.All)
        {
            if (!_carriers.TryGet(placement.CarrierId, out _))
            {
                findings.Add(new ReconciliationFinding(
                    "missing",
                    placement.PlacementId,
                    $"载体 '{placement.CarrierId}' 未注册：该位置声明无数据面"));
            }
        }

        foreach (var carrier in _carriers.All)
        {
            var inCarrier = _placements.ByCarrier(carrier.CarrierId);
            var keys = await carrier.EnumerateKeysAsync(null, cancellationToken).ConfigureAwait(false);

            foreach (var key in keys)
            {
                var matched = inCarrier
                    .Where(p => KeyTemplateEvaluator.TryParseValues(p.KeyTemplate, key, out _))
                    .ToArray();

                if (matched.Length == 0)
                {
                    findings.Add(new ReconciliationFinding(
                        "orphan",
                        key,
                        $"载体 '{carrier.CarrierId}' 上的键不匹配任何位置声明（缺失声明或键模板漂移）"));
                    continue;
                }

                if (matched.Length > 1)
                {
                    findings.Add(new ReconciliationFinding(
                        "ambiguous",
                        key,
                        $"键同时匹配 {matched.Length} 个位置声明: {string.Join(",", matched.Select(p => p.PlacementId))}"));
                }
            }
        }

        CollectDrift(spec, findings);

        foreach (var finding in findings)
        {
            _diagnostics.Report(new NodeDiagnostic(
                $"reconcile.{finding.Kind}",
                NodeDiagnosticSeverity.Warning,
                finding.Detail,
                finding.Subject));
        }

        return new ReconciliationReport(findings);
    }

    /// <summary>声明自检（不触载体）：位置声明的键模板必须都能由身份规范求值。</summary>
    public ReconciliationReport ValidateTemplates(NodeFrameworkSpec spec)
    {
        var findings = new List<ReconciliationFinding>();
        CollectDrift(spec, findings);
        return new ReconciliationReport(findings);
    }

    /// <summary>键模板漂移收集单源（数据面与声明自检共用；位置面 = 注入的注册表）。</summary>
    private void CollectDrift(NodeFrameworkSpec spec, List<ReconciliationFinding> findings)
    {
        foreach (var placement in _placements.All)
        {
            foreach (var placeholder in KeyTemplateEvaluator.Placeholders(placement.KeyTemplate))
            {
                if (!spec.Identity.Placeholders.Contains(placeholder, StringComparer.Ordinal))
                {
                    findings.Add(new ReconciliationFinding(
                        "drift",
                        placement.PlacementId,
                        $"键模板字段 '{placeholder}' 不在身份规范中（模板漂移）"));
                }
            }
        }
    }
}
