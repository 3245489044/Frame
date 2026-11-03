using Plate.DslMethod;
using Plate.DslMethod.Model;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A：关系声明面（<c>[Relation]</c> R1–R8 / <c>[Invariant]</c> I1–I6 / <c>[Derive]</c> D1–D7）。
/// <para>判据载体 = 声明结构事实（边/端点/流/词表/路径首尾相接）；禁以执行者叙述作判据。</para>
/// </summary>
internal sealed class RelationGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.relation";

    /// <summary>归属边集合（关系操作的唯一合法边域；与 hierarchy.ushar 的 contains= 对账）。</summary>
    private static readonly string[] OwnershipEdges =
        { "cross_db_link", "history_owns_task", "session_owns", "task_owns_task" };

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var declaredEntities = model.Declarations.NodeNames;
        var spaceIds = new HashSet<string>(
            model.Declarations.OfKind(DslSymbolKind.Space).Select(d => d.Name), StringComparer.Ordinal);
        var obsNames = new HashSet<string>(
            model.Declarations.OfKind(DslSymbolKind.Obs).Select(d => d.Name), StringComparer.Ordinal);

        CheckRelations(model, sink, declaredEntities, spaceIds, obsNames);
        CheckInvariants(model, sink, declaredEntities);
        CheckDerivations(model, sink, declaredEntities);
    }

    // ── R1–R8：关系操作 ────────────────────────────────────────────────

    private static void CheckRelations(
        UsharpGateModel model,
        IConversionDiagnosticSink sink,
        IReadOnlySet<string> declaredEntities,
        HashSet<string> spaceIds,
        HashSet<string> obsNames)
    {
        var seen = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var mountCountByEdge = new Dictionary<string, int>(StringComparer.Ordinal);
        var unmountCountByEdge = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var relation in model.Relations)
        {
            ReportUnparsed(sink, "[Relation]", relation.OperationId, relation.UnparsedLines,
                relation.SourceFile, relation.LineNumber);

            if (seen.TryGetValue(relation.OperationId, out var origin))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationDuplicate,
                    $"[Relation] '{relation.OperationId}' 重复声明（首次于 {origin.File}:{origin.Line}）",
                    relation.SourceFile, relation.LineNumber));
            else
                seen[relation.OperationId] = (relation.SourceFile, relation.LineNumber);

            var hasEdge = model.Declarations.TryGetEdgeDecl(relation.Edge, out var edgeDecl);
            if (!hasEdge)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationEdgeUndeclared,
                    $"[Relation] '{relation.OperationId}'：edge='{relation.Edge}' 未在 [Edge] 声明",
                    relation.SourceFile, relation.LineNumber));

            if (!declaredEntities.Contains(relation.Source))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationEndpointMismatch,
                    $"[Relation] '{relation.OperationId}'：src='{relation.Source}' 未在 [Node] 声明",
                    relation.SourceFile, relation.LineNumber));
            if (!declaredEntities.Contains(relation.Target))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationEndpointMismatch,
                    $"[Relation] '{relation.OperationId}'：tgt='{relation.Target}' 未在 [Node] 声明",
                    relation.SourceFile, relation.LineNumber));

            if (hasEdge) CheckEdgeEndpoint(sink, relation, edgeDecl);
            CheckFlow(sink, model, relation);
            CheckNamespace(sink, relation, spaceIds);
            CheckRelationVocab(sink, relation);
            CheckEmit(sink, relation, obsNames);

            if (string.Equals(relation.Intent, UsharpLanguage.RelationIntentEnsure, StringComparison.Ordinal))
                mountCountByEdge[relation.Edge] = mountCountByEdge.TryGetValue(relation.Edge, out var n) ? n + 1 : 1;
            else if (string.Equals(relation.Intent, UsharpLanguage.RelationIntentRemove, StringComparison.Ordinal))
                unmountCountByEdge[relation.Edge] = unmountCountByEdge.TryGetValue(relation.Edge, out var m) ? m + 1 : 1;
        }

        // E2 作用域 = 本编译单元内的归属边（有 [Relation] 或 [Edge] 声明）。
        // 工作流单文件（agent-loop.ushar 等）不含关系声明 ⇒ 不承担归属边完整性
        //（归属边完整性由 relation-ops.ushar 的编译单元负责；单文件工作流编译不得因跨文件声明缺失而红）。
        var scopedEdges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in model.Relations) scopedEdges.Add(relation.Edge);
        foreach (var edge in OwnershipEdges)
        {
            if (model.Declarations.TryGetEdgeDecl(edge, out _)) scopedEdges.Add(edge);
        }

        if (scopedEdges.Count == 0) return;

        // E2：豁免条件 = 该边「无任何 mount_/unmount_ 声明」∧「被单跳 [Derive] 覆盖」。
        // 仅凭存在单跳 [Derive] 就豁免会让「删掉 mount_node_task」仍绿（假绿）。
        var derivedEdges = DerivedEdges(model);
        foreach (var edge in OwnershipEdges)
        {
            if (!scopedEdges.Contains(edge)) continue;

            var mountCount = mountCountByEdge.TryGetValue(edge, out var c) ? c : 0;
            var unmountCount = unmountCountByEdge.TryGetValue(edge, out var u) ? u : 0;
            if (mountCount == 0 && unmountCount == 0 && derivedEdges.Contains(edge)) continue;

            if (mountCount != 1)
            {
                var (file, line) = model.Declarations.TryGetEdgeDecl(edge, out var edgeDecl)
                    ? (edgeDecl.SourceFile, edgeDecl.LineNumber)
                    : (model.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 1);
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationMountMissing,
                    $"归属边 '{edge}' 必须恰有一个 mount_* operation（实测 {mountCount} 个），"
                    + "或该边无任何 mount_/unmount_ 且由单跳 [Derive] 派生",
                    file, line));
            }
        }
    }

    private static void CheckEdgeEndpoint(
        IConversionDiagnosticSink sink, UsharpRelationDecl relation, DslDeclaration edgeDecl)
    {
        var edgeSrc = (edgeDecl.Field(UsharpLanguage.FieldSrc) ?? string.Empty).Trim();
        var edgeTgt = (edgeDecl.Field(UsharpLanguage.FieldTgt) ?? string.Empty).Trim();
        if (!MatchesEndpoint(edgeSrc, relation.Source))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationEndpointMismatch,
                $"[Relation] '{relation.OperationId}'：src='{relation.Source}' 与 [Edge] {relation.Edge} src='{edgeSrc}' 不一致",
                relation.SourceFile, relation.LineNumber));
        if (!MatchesEndpoint(edgeTgt, relation.Target))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationEndpointMismatch,
                $"[Relation] '{relation.OperationId}'：tgt='{relation.Target}' 与 [Edge] {relation.Edge} tgt='{edgeTgt}' 不一致",
                relation.SourceFile, relation.LineNumber));
    }

    private static void CheckFlow(
        IConversionDiagnosticSink sink, UsharpGateModel model, UsharpRelationDecl relation)
    {
        var flow = model.FlowById(relation.Flow);
        if (flow is null)
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationFlowUnknown,
                $"[Relation] '{relation.OperationId}'：flow='{relation.Flow}' 未声明",
                relation.SourceFile, relation.LineNumber));
            return;
        }

        if (!FlowRunsRelationApply(model, flow))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationFlowUnknown,
                $"[Relation] '{relation.OperationId}'：flow='{relation.Flow}' 的执行体不是 fn={UsharpLanguage.RelationApplyFunction}"
                + "（要求恰一步且该步节点 fn=relation_apply）",
                relation.SourceFile, relation.LineNumber));
    }

    private static void CheckNamespace(
        IConversionDiagnosticSink sink, UsharpRelationDecl relation, HashSet<string> spaceIds)
    {
        if (IsKnownNamespace(relation.Namespace, spaceIds)) return;
        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationNamespaceUnknown,
            $"[Relation] '{relation.OperationId}'：namespace='{relation.Namespace}' 未声明"
            + $"（允许 {UsharpLanguage.RelationNamespaceWorkflow} / {UsharpLanguage.RelationNamespaceNone} / 已声明空间 id）",
            relation.SourceFile, relation.LineNumber));
    }

    private static void CheckRelationVocab(IConversionDiagnosticSink sink, UsharpRelationDecl relation)
    {
        if (relation.Duplicate.Length > 0
            && !UsharpLanguage.RelationDuplicateModes.Contains(relation.Duplicate, StringComparer.Ordinal))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationFieldInvalid,
                $"[Relation] '{relation.OperationId}'：duplicate='{relation.Duplicate}' 非法",
                relation.SourceFile, relation.LineNumber));
        if (relation.OnMissing.Length > 0
            && !UsharpLanguage.RelationOnMissingModes.Contains(relation.OnMissing, StringComparer.Ordinal))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationFieldInvalid,
                $"[Relation] '{relation.OperationId}'：on_missing='{relation.OnMissing}' 非法",
                relation.SourceFile, relation.LineNumber));
        if (relation.Arity.Length > 0
            && !UsharpLanguage.RelationArities.Contains(relation.Arity, StringComparer.Ordinal))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationFieldInvalid,
                $"[Relation] '{relation.OperationId}'：arity='{relation.Arity}' 非法",
                relation.SourceFile, relation.LineNumber));
        // 删除意图必须显式声明（唯一判据 = 声明事实；禁 operation 前缀推断）。
        if (!UsharpLanguage.RelationIntents.Contains(relation.Intent, StringComparer.Ordinal))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationFieldInvalid,
                $"[Relation] '{relation.OperationId}'：intent='{relation.Intent}' 非法或缺失（须 ensure|remove）",
                relation.SourceFile, relation.LineNumber));
    }

    private static void CheckEmit(
        IConversionDiagnosticSink sink, UsharpRelationDecl relation, HashSet<string> obsNames)
    {
        if (relation.Emit is { Length: > 0 } emit && !obsNames.Contains(emit))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RelationEmitUndeclared,
                $"[Relation] '{relation.OperationId}'：emit='{emit}' 未在 [Obs] 声明",
                relation.SourceFile, relation.LineNumber));
    }

    private static bool MatchesEndpoint(string declared, string actual)
        => declared.Length == 0 || string.Equals(declared, "any", StringComparison.Ordinal)
           || string.Equals(declared, actual, StringComparison.Ordinal);

    private static bool IsKnownNamespace(string value, HashSet<string> spaceIds)
        => string.Equals(value, UsharpLanguage.RelationNamespaceWorkflow, StringComparison.Ordinal)
           || string.Equals(value, UsharpLanguage.RelationNamespaceNone, StringComparison.Ordinal)
           || spaceIds.Contains(value);

    /// <summary>流是否恰一步且该步节点 <c>fn=relation_apply</c>（唯一执行体判据）。</summary>
    private static bool FlowRunsRelationApply(UsharpGateModel model, UsharpFlowDecl flow)
    {
        if (flow.Steps.Count != 1) return false;
        var node = model.Declarations.Nodes.FirstOrDefault(d =>
            string.Equals(d.Name, flow.Steps[0].Key, StringComparison.Ordinal));
        return node is not null
               && string.Equals((node.Field("fn") ?? string.Empty).Trim(),
                   UsharpLanguage.RelationApplyFunction, StringComparison.Ordinal);
    }

    /// <summary>单跳 [Derive] 的边集（派生视图，豁免 R7 的 mount_* 要求）。</summary>
    private static HashSet<string> DerivedEdges(UsharpGateModel model)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var derive in model.Derivations)
            if (derive.Via.Count == 1) set.Add(derive.Via[0]);
        return set;
    }

    // ── I1–I6：关系不变量 ──────────────────────────────────────────────

    private static void CheckInvariants(
        UsharpGateModel model,
        IConversionDiagnosticSink sink,
        IReadOnlySet<string> declaredEntities)
    {
        var seen = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var shapes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var invariant in model.Invariants)
        {
            ReportUnparsed(sink, "[Invariant]", invariant.InvariantId, invariant.UnparsedLines,
                invariant.SourceFile, invariant.LineNumber);

            if (seen.TryGetValue(invariant.InvariantId, out var origin))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.InvariantDuplicate,
                    $"[Invariant] '{invariant.InvariantId}' 重复声明（首次于 {origin.File}:{origin.Line}）",
                    invariant.SourceFile, invariant.LineNumber));
            else
                seen[invariant.InvariantId] = (invariant.SourceFile, invariant.LineNumber);

            if (!model.Declarations.TryGetEdgeDecl(invariant.Edge, out _))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.InvariantEdgeUndeclared,
                    $"[Invariant] '{invariant.InvariantId}'：edge='{invariant.Edge}' 未声明",
                    invariant.SourceFile, invariant.LineNumber));

            if (!UsharpLanguage.InvariantKinds.Contains(invariant.Kind, StringComparer.Ordinal))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.InvariantKindUnknown,
                    $"[Invariant] '{invariant.InvariantId}'：kind='{invariant.Kind}' 非法",
                    invariant.SourceFile, invariant.LineNumber));

            if (!UsharpLanguage.InvariantOnViolations.Contains(invariant.OnViolation, StringComparer.Ordinal))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.InvariantOnViolationInvalid,
                    $"[Invariant] '{invariant.InvariantId}'：on_violation='{invariant.OnViolation}' 非法",
                    invariant.SourceFile, invariant.LineNumber));

            if (invariant.Scope is { Length: > 0 } scope && !declaredEntities.Contains(scope))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.InvariantScopeUndeclared,
                    $"[Invariant] '{invariant.InvariantId}'：scope='{scope}' 未在 [Node] 声明",
                    invariant.SourceFile, invariant.LineNumber));

            var shape = $"{invariant.Edge}|{invariant.Kind}|{invariant.Scope ?? string.Empty}";
            if (!shapes.Add(shape))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.InvariantDuplicateShape,
                    $"[Invariant] '{invariant.InvariantId}'：(edge,kind,scope) 与既有声明重复",
                    invariant.SourceFile, invariant.LineNumber));
        }
    }

    // ── D1–D7：派生成员集 ──────────────────────────────────────────────

    private static void CheckDerivations(
        UsharpGateModel model,
        IConversionDiagnosticSink sink,
        IReadOnlySet<string> declaredEntities)
    {
        var seen = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var shapes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var derive in model.Derivations)
        {
            ReportUnparsed(sink, "[Derive]", derive.DeriveId, derive.UnparsedLines,
                derive.SourceFile, derive.LineNumber);

            if (seen.TryGetValue(derive.DeriveId, out var origin))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveDuplicate,
                    $"[Derive] '{derive.DeriveId}' 重复声明（首次于 {origin.File}:{origin.Line}）",
                    derive.SourceFile, derive.LineNumber));
            else
                seen[derive.DeriveId] = (derive.SourceFile, derive.LineNumber);

            if (!declaredEntities.Contains(derive.From))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveEndpointUndeclared,
                    $"[Derive] '{derive.DeriveId}'：from='{derive.From}' 未在 [Node] 声明",
                    derive.SourceFile, derive.LineNumber));
            if (!declaredEntities.Contains(derive.To))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveEndpointUndeclared,
                    $"[Derive] '{derive.DeriveId}'：to='{derive.To}' 未在 [Node] 声明",
                    derive.SourceFile, derive.LineNumber));

            var edges = new List<DslDeclaration>();
            foreach (var edge in derive.Via)
            {
                if (!model.Declarations.TryGetEdgeDecl(edge, out var decl))
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveEdgeUndeclared,
                        $"[Derive] '{derive.DeriveId}'：via 边 '{edge}' 未声明",
                        derive.SourceFile, derive.LineNumber));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(decl.Field("dep")))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveEdgeDeprecated,
                        $"[Derive] '{derive.DeriveId}'：via 含已废弃边 '{edge}'",
                        derive.SourceFile, derive.LineNumber));

                edges.Add(decl);
            }

            if (edges.Count == derive.Via.Count && edges.Count > 0)
                CheckViaChain(sink, derive, edges);

            if (!UsharpLanguage.DeriveOps.Contains(derive.Op, StringComparer.Ordinal))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveFieldInvalid,
                    $"[Derive] '{derive.DeriveId}'：op='{derive.Op}' 非法",
                    derive.SourceFile, derive.LineNumber));
            if (!UsharpLanguage.DeriveNormalizes.Contains(derive.Normalize, StringComparer.Ordinal))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveFieldInvalid,
                    $"[Derive] '{derive.DeriveId}'：normalize='{derive.Normalize}' 非法",
                    derive.SourceFile, derive.LineNumber));
            if (!UsharpLanguage.DeriveOrders.Contains(derive.Order, StringComparer.Ordinal))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveFieldInvalid,
                    $"[Derive] '{derive.DeriveId}'：order='{derive.Order}' 非法",
                    derive.SourceFile, derive.LineNumber));
            if (derive.Empty.Count == 0)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveFieldInvalid,
                    $"[Derive] '{derive.DeriveId}'：empty= 缺失（空集须给合法空因，禁静默空集）",
                    derive.SourceFile, derive.LineNumber));

            var shape = $"{derive.From}|{derive.To}|{string.Join(",", derive.Via)}";
            if (!shapes.Add(shape))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveDuplicateShape,
                    $"[Derive] '{derive.DeriveId}'：(from,to,via) 与既有声明重复",
                    derive.SourceFile, derive.LineNumber));
        }
    }

    /// <summary>D4：via 首尾相接（首边 src=from、尾边 tgt=to、相邻 tgt=src；<c>any</c> 为通配）。</summary>
    private static void CheckViaChain(
        IConversionDiagnosticSink sink, UsharpDeriveDecl derive, IReadOnlyList<DslDeclaration> edges)
    {
        static string End(DslDeclaration d, string key) => (d.Field(key) ?? string.Empty).Trim();

        var firstSrc = End(edges[0], UsharpLanguage.FieldSrc);
        var lastTgt = End(edges[^1], UsharpLanguage.FieldTgt);
        if (!ChainEndpointMatches(firstSrc, derive.From))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveViaBroken,
                $"[Derive] '{derive.DeriveId}'：via 首边 src='{firstSrc}' ≠ from='{derive.From}'",
                derive.SourceFile, derive.LineNumber));
        if (!ChainEndpointMatches(lastTgt, derive.To))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveViaBroken,
                $"[Derive] '{derive.DeriveId}'：via 尾边 tgt='{lastTgt}' ≠ to='{derive.To}'",
                derive.SourceFile, derive.LineNumber));

        for (var i = 1; i < edges.Count; i++)
        {
            var prevTgt = End(edges[i - 1], UsharpLanguage.FieldTgt);
            var nextSrc = End(edges[i], UsharpLanguage.FieldSrc);
            if (!ChainEndpointMatches(prevTgt, nextSrc) && !ChainEndpointMatches(nextSrc, prevTgt))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeriveViaBroken,
                    $"[Derive] '{derive.DeriveId}'：via 第 {i} 跳首尾不相接"
                    + $"（前跳 tgt='{prevTgt}' vs 后跳 src='{nextSrc}'）",
                    derive.SourceFile, derive.LineNumber));
        }
    }

    private static bool ChainEndpointMatches(string left, string right)
        => left.Length == 0 || right.Length == 0
           || string.Equals(left, "any", StringComparison.Ordinal)
           || string.Equals(right, "any", StringComparison.Ordinal)
           || string.Equals(left, right, StringComparison.Ordinal);

    // ── 公共：未解析续行 ───────────────────────────────────────────────

    private static void ReportUnparsed(
        IConversionDiagnosticSink sink, string prefix, string id,
        IReadOnlyList<string> lines, string file, int line)
    {
        foreach (var text in lines)
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DocumentBodyLineUnparsed,
                $"{prefix} {id}: 无法解析的续行 '{text}'", file, line));
    }
}
