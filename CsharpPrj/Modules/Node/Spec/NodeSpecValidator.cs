using System.Collections.Generic;
using System.Linq;
using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Identity;

namespace Modules.Node.Spec;

public sealed record NodeSpecViolation(string Code, NodeDiagnosticSeverity Severity, string Message, string? Subject = null);

public sealed record NodeSpecValidationReport(IReadOnlyList<NodeSpecViolation> Violations)
{
    public bool IsValid => Violations.All(v => v.Severity != NodeDiagnosticSeverity.Error);

    public IReadOnlyList<NodeSpecViolation> Errors =>
        Violations.Where(v => v.Severity == NodeDiagnosticSeverity.Error).ToArray();

    public IReadOnlyList<NodeSpecViolation> Warnings =>
        Violations.Where(v => v.Severity == NodeDiagnosticSeverity.Warning).ToArray();
}

/// <summary>链声明的最小中立形状（D-1 案 A：不引上层 DTO）。</summary>
public sealed record NodeChainNode(
    string NodeKey,
    string? NextNodeKey,
    IReadOnlyList<string> ParameterKeys,
    IReadOnlyList<string> ResultKeys,
    bool IsEntry,
    bool IsExit);

public sealed record NodeChainDocument(string ChainId, IReadOnlyList<NodeChainNode> Nodes);

/// <summary>
/// 装配期校验（fail-fast，G10 收编）：身份模板可求值 / 同域模板可区分 / writer 一对一 /
/// 位置键含序数占位 / carrier 已注册（未注册 → Unbound 告警，不阻断其它位置）/ 链无环 ∧ 无悬空 ∧ 入口出口存在。
/// 不静默修正数据。
/// </summary>
public sealed class NodeSpecValidator
{
    private readonly ICarrierRegistry _carriers;
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeSpecValidator(ICarrierRegistry carriers, INodeDiagnosticsSink? diagnostics = null)
    {
        _carriers = carriers;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public NodeSpecValidationReport Validate(NodeFrameworkSpec spec, IReadOnlyList<NodeChainDocument>? chains = null)
    {
        var violations = new List<NodeSpecViolation>();

        ValidateIdentity(spec, violations);
        ValidatePlacements(spec, violations);
        ValidateChains(chains ?? Array.Empty<NodeChainDocument>(), violations, requireEntry: true, requireExit: true);

        Report(violations);

        return new NodeSpecValidationReport(violations);
    }

    /// <summary>
    /// 链判据<b>唯一算法出处</b>（治 P19：宿主/语言/框架不得各写一套）：无环 / 无悬空 / 键不重复 / 入口出口存在。
    /// <paramref name="requireEntry"/>=<paramref name="requireExit"/>=<c>false</c> 即只判环与悬空（宿主既有口径，供等价迁移）。
    /// </summary>
    public NodeSpecValidationReport ValidateChains(
        IReadOnlyList<NodeChainDocument> chains,
        bool requireEntry = true,
        bool requireExit = true)
    {
        var violations = new List<NodeSpecViolation>();
        ValidateChains(chains, violations, requireEntry, requireExit);
        Report(violations);
        return new NodeSpecValidationReport(violations);
    }

    /// <summary>
    /// 链判据（<b>无载体依赖的纯静态入口</b>）：供语言级 / 编译器（宿主）复用同一算法，治 P19「校验双实现」。
    /// 语义与实例 <c>ValidateChains(chains, requireEntry, requireExit)</c> 一致，但不经载体注册表、不写诊断。
    /// </summary>
    public static NodeSpecValidationReport ValidateChainDocuments(
        IReadOnlyList<NodeChainDocument> chains,
        bool requireEntry = true,
        bool requireExit = true)
    {
        var violations = new List<NodeSpecViolation>();
        ValidateChains(chains, violations, requireEntry, requireExit);
        return new NodeSpecValidationReport(violations);
    }

    private void Report(IReadOnlyList<NodeSpecViolation> violations)
    {
        foreach (var violation in violations)
        {
            _diagnostics.Report(new NodeDiagnostic(
                violation.Code,
                violation.Severity,
                violation.Message,
                violation.Subject));
        }
    }

    private static void ValidateIdentity(NodeFrameworkSpec spec, List<NodeSpecViolation> violations)
    {
        foreach (var contextKey in spec.Identity.ContextKeys)
        {
            if (!spec.ContextKeys.Contains(contextKey, StringComparer.Ordinal))
            {
                violations.Add(new NodeSpecViolation(
                    "spec.identity-context-undeclared",
                    NodeDiagnosticSeverity.Error,
                    $"身份模板的上下文段 '{contextKey}' 未在 context 中声明",
                    spec.Identity.Template));
            }
        }

        if (!spec.Identity.HasSequencePlaceholder && spec.Identity.Placeholders.Count > 0)
        {
            violations.Add(new NodeSpecViolation(
                "spec.identity-sequence-missing",
                NodeDiagnosticSeverity.Warning,
                "身份模板不含序数占位：顺序将由边/外部序号表达（违 N6）",
                spec.Identity.Template));
        }
    }

    private void ValidatePlacements(NodeFrameworkSpec spec, List<NodeSpecViolation> violations)
    {
        foreach (var domain in spec.Placements.All.Select(p => p.Domain).Distinct(StringComparer.Ordinal))
        {
            var inDomain = spec.Placements.ByDomain(domain);

            for (var i = 0; i < inDomain.Count; i++)
            {
                for (var j = i + 1; j < inDomain.Count; j++)
                {
                    if (!KeyTemplateEvaluator.TemplatesDistinguishable(inDomain[i].KeyTemplate, inDomain[j].KeyTemplate))
                    {
                        violations.Add(new NodeSpecViolation(
                            "spec.template-indistinguishable",
                            NodeDiagnosticSeverity.Error,
                            $"同域键模板不可区分: {inDomain[i].PlacementId} / {inDomain[j].PlacementId}",
                            domain));
                    }
                }
            }

            // 同域 writer 复用 = **单写者多落点**（合法，且为段域既有设计：两位置同属一次切分物化，
            // 写门目标共用 writer-segment —— 见 NodeDomainCatalog.TryResolveSingleWriter 要求「写门目标唯一写者」）。
            // 故此处只能**可见化**（告警），不得判 Error：真正的不变式（位置 → 写者唯一）由 PlacementRegistry
            // 的 PlacementId 主键天然保证；把「写者复用」当违规会与生产声明冲突（本判据从未接线，缺陷未暴露）。
            foreach (var group in inDomain.GroupBy(p => p.WriterId, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                violations.Add(new NodeSpecViolation(
                    "spec.writer-shared",
                    NodeDiagnosticSeverity.Warning,
                    $"同域内 writer '{group.Key}' 被 {group.Count()} 个位置共用（单写者多落点：须为同一次物化的多个落点）",
                    domain));
            }
        }

        foreach (var placement in spec.Placements.All)
        {
            // 位置键字段域 = 身份占位符 ∪ 本位置 local 坐标（N2：位置不改变身份；异坐标空间须显式声明 local）。
            var allowedFields = new HashSet<string>(spec.Identity.Placeholders, StringComparer.Ordinal);
            foreach (var local in placement.LocalCoordinatesOrEmpty)
            {
                allowedFields.Add(local);
            }

            var keyFields = KeyTemplateEvaluator.Placeholders(placement.KeyTemplate);
            foreach (var placeholder in keyFields)
            {
                if (!allowedFields.Contains(placeholder))
                {
                    violations.Add(new NodeSpecViolation(
                        "spec.placement-template-unknown-field",
                        NodeDiagnosticSeverity.Error,
                        $"位置 '{placement.PlacementId}' 键模板使用未声明字段: {placeholder}"
                        + "（须为身份占位符或该位置声明的 local 坐标）",
                        placement.KeyTemplate));
                }
            }

            // local 自洽：与身份占位符重名 ⇒ 同坐标两名（静默漂移温床）；声明却未用 ⇒ 死声明。
            foreach (var local in placement.LocalCoordinatesOrEmpty)
            {
                if (spec.Identity.Placeholders.Contains(local, StringComparer.Ordinal))
                {
                    violations.Add(new NodeSpecViolation(
                        "spec.placement-local-coordinate-shadow",
                        NodeDiagnosticSeverity.Error,
                        $"位置 '{placement.PlacementId}' 的 local 坐标 '{local}' 与身份占位符重名（局部坐标不得遮蔽身份坐标）",
                        placement.PlacementId));
                }
                else if (!keyFields.Contains(local, StringComparer.Ordinal))
                {
                    violations.Add(new NodeSpecViolation(
                        "spec.placement-local-coordinate-unused",
                        NodeDiagnosticSeverity.Warning,
                        $"位置 '{placement.PlacementId}' 声明的 local 坐标 '{local}' 未出现在键模板（死声明）",
                        placement.KeyTemplate));
                }
            }

            if (!KeyTemplateEvaluator.ContainsSequencePlaceholder(placement.KeyTemplate, spec.Identity.SequenceKeys))
            {
                violations.Add(new NodeSpecViolation(
                    "spec.placement-template-no-ordinal",
                    NodeDiagnosticSeverity.Warning,
                    $"位置 '{placement.PlacementId}' 键模板不含序数占位（L-4：序数须由 ISequenceAuthority 填充）",
                    placement.KeyTemplate));
            }

            if (!_carriers.TryGet(placement.CarrierId, out _))
            {
                violations.Add(new NodeSpecViolation(
                    "spec.carrier-unbound",
                    NodeDiagnosticSeverity.Warning,
                    $"位置 '{placement.PlacementId}' 的载体 '{placement.CarrierId}' 未注册：该位置记 Unbound（不阻断其它位置）",
                    placement.PlacementId));
            }
        }
    }

    private static void ValidateChains(
        IReadOnlyList<NodeChainDocument> chains,
        List<NodeSpecViolation> violations,
        bool requireEntry,
        bool requireExit)
    {
        foreach (var chain in chains)
        {
            if (chain.Nodes.Count == 0)
            {
                violations.Add(new NodeSpecViolation(
                    "spec.chain-empty",
                    NodeDiagnosticSeverity.Error,
                    "链为空",
                    chain.ChainId));
                continue;
            }

            var keys = chain.Nodes.Select(n => n.NodeKey).ToArray();
            var keySet = new HashSet<string>(keys, StringComparer.Ordinal);

            foreach (var duplicate in keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                violations.Add(new NodeSpecViolation(
                    "spec.chain-duplicate-key",
                    NodeDiagnosticSeverity.Error,
                    $"链内节点键重复: {duplicate}",
                    chain.ChainId));
            }

            foreach (var node in chain.Nodes)
            {
                if (node.NextNodeKey is { Length: > 0 } next && !keySet.Contains(next))
                {
                    violations.Add(new NodeSpecViolation(
                        "spec.chain-dangling",
                        NodeDiagnosticSeverity.Error,
                        $"悬空引用：{node.NodeKey} → {next}（指向不存在的节点）",
                        chain.ChainId));
                }
            }

            if (requireEntry && !chain.Nodes.Any(n => n.IsEntry))
            {
                violations.Add(new NodeSpecViolation(
                    "spec.chain-entry-missing",
                    NodeDiagnosticSeverity.Error,
                    "链缺少入口节点",
                    chain.ChainId));
            }

            if (requireExit && !chain.Nodes.Any(n => n.IsExit))
            {
                violations.Add(new NodeSpecViolation(
                    "spec.chain-exit-missing",
                    NodeDiagnosticSeverity.Error,
                    "链缺少出口节点",
                    chain.ChainId));
            }

            DetectCycle(chain, violations);
        }
    }

    private static void DetectCycle(NodeChainDocument chain, List<NodeSpecViolation> violations)
    {
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in chain.Nodes)
        {
            if (node.NextNodeKey is { Length: > 0 } target && !next.ContainsKey(node.NodeKey))
            {
                next[node.NodeKey] = target;
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in chain.Nodes.Select(n => n.NodeKey))
        {
            var path = new List<string>();
            var current = start;

            while (true)
            {
                if (path.Contains(current, StringComparer.Ordinal))
                {
                    var cycleStart = path.IndexOf(current);
                    var cycle = string.Join(" → ", path.Skip(cycleStart)) + " → " + current;
                    violations.Add(new NodeSpecViolation(
                        "spec.chain-cycle",
                        NodeDiagnosticSeverity.Error,
                        $"链存在环: {cycle}",
                        chain.ChainId));
                    break;
                }

                if (visited.Contains(current))
                {
                    break;
                }

                path.Add(current);
                visited.Add(current);

                if (!next.TryGetValue(current, out var following))
                {
                    break;
                }

                current = following;
            }
        }
    }
}
