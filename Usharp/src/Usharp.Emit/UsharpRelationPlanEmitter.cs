using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>一次关系计划发射的产物：<c>relation.plan.generated.json</c> + 发射期诊断。</summary>
public sealed record UsharpRelationPlanResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 关系声明面 → <c>relation.plan.generated.json</c>（operations / invariants / derivations）。
/// <para>
/// <b>不含 <c>policy</c></b>：边策略由桥接层从 <c>ReferenceSchema</c> join（避免 <see cref="Usharp.Emit"/> 依赖 Plate Schema）。
/// 无任何 <c>[Relation]</c> ⇒ 不产出 + <c>US8122</c>（可见化，不静默给空计划）。
/// </para>
/// <para>保插入序（序 = 声明序）⇒ 等价门可逐字段比对（对齐 <see cref="UsharpRefEdgeEmitter"/> 范式）。</para>
/// </summary>
public static class UsharpRelationPlanEmitter
{
    /// <summary>发射本编译的关系计划（无声明 ⇒ <c>null</c> + <c>US8122</c>）。</summary>
    public static UsharpRelationPlanResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        if (compilation.Relations.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.RelationPlanNotProducible,
                "关系计划不可产出：本编译无任何 [Relation] 声明（关系操作的唯一声明面缺失）",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 0));
            return new UsharpRelationPlanResult(null, diagnostics);
        }

        return new UsharpRelationPlanResult(Serialize(compilation), diagnostics);
    }

    private static string Serialize(UsharpCompilation compilation)
    {
        var operations = new JsonArray();
        foreach (var relation in compilation.Relations)
        {
            var item = new JsonObject
            {
                ["id"] = relation.OperationId,
                ["edge"] = relation.Edge,
                ["src"] = relation.Source,
                ["tgt"] = relation.Target,
                ["flow"] = relation.Flow,
                ["namespace"] = relation.Namespace,
                ["exists"] = UsharpJson.Array(relation.Exists),
                ["intent"] = relation.Intent,
            };

            if (relation.Display is { Length: > 0 } display) item["display"] = display;
            if (relation.Duplicate.Length > 0) item["duplicate"] = relation.Duplicate;
            if (relation.OnMissing.Length > 0) item["onMissing"] = relation.OnMissing;
            if (relation.Arity.Length > 0) item["arity"] = relation.Arity;
            if (relation.Emit is { Length: > 0 } emit) item["emit"] = emit;

            operations.Add(item);
        }

        var invariants = new JsonArray();
        foreach (var invariant in compilation.Invariants)
        {
            var item = new JsonObject
            {
                ["id"] = invariant.InvariantId,
                ["kind"] = invariant.Kind,
                ["edge"] = invariant.Edge,
                ["onViolation"] = invariant.OnViolation,
            };
            if (invariant.Display is { Length: > 0 } display) item["display"] = display;
            if (invariant.Scope is { Length: > 0 } scope) item["scope"] = scope;
            invariants.Add(item);
        }

        var derivations = new JsonArray();
        foreach (var derive in compilation.Derivations)
        {
            var item = new JsonObject
            {
                ["id"] = derive.DeriveId,
                ["from"] = derive.From,
                ["to"] = derive.To,
                ["via"] = UsharpJson.Array(derive.Via),
                ["op"] = derive.Op,
                ["normalize"] = derive.Normalize,
                ["order"] = derive.Order,
                ["empty"] = UsharpJson.Array(derive.Empty),
            };
            if (derive.Display is { Length: > 0 } display) item["display"] = display;
            derivations.Add(item);
        }

        var root = new JsonObject
        {
            ["version"] = "1.0",
            ["operations"] = operations,
            ["invariants"] = invariants,
            ["derivations"] = derivations,
        };

        return root.ToJsonString();
    }
}
