using System.Text.Json.Nodes;
using Operation.DataStoreDriven.Relations;
using Usharp.Emit;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// Usharp 关系声明面 → Frame 中性 <see cref="RelationPlan"/>（唯一桥接；DataStoreDriven 不引 Usharp）。
/// <para>产物：<c>relation.plan.generated.json</c>（书写 = <see cref="UsharpRelationPlanEmitter"/>）。</para>
/// </summary>
public static class DeclaredRelationPlan
{
    /// <summary>由编译产物构建计划（无声明 ⇒ <see cref="RelationPlan.Empty"/>，可见化由调用方判定）。</summary>
    public static RelationPlan BuildFromCompilation(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var operations = compilation.Relations
            .Select(r => new RelationOperation(
                r.OperationId, r.Display, r.Edge, r.Source, r.Target, r.Flow, r.Namespace,
                r.Exists, r.Duplicate, r.OnMissing, r.Arity, r.Emit))
            .ToList();

        var invariants = compilation.Invariants
            .Select(i => new RelationInvariant(i.InvariantId, i.Kind, i.Edge, i.OnViolation, i.Scope))
            .ToList();

        var derivations = compilation.Derivations
            .Select(d => new RelationDerivation(
                d.DeriveId, d.From, d.To, d.Via, d.Op, d.Normalize, d.Order, d.Empty))
            .ToList();

        return operations.Count == 0 && invariants.Count == 0 && derivations.Count == 0
            ? RelationPlan.Empty
            : new RelationPlan("1.0", operations, invariants, derivations);
    }

    /// <summary>编译并发射计划 JSON（一次编译，声明面 → 产物文本；诊断随结果返回）。</summary>
    public static UsharpRelationPlanResult EmitFromCompilation(UsharpCompilation compilation)
        => UsharpRelationPlanEmitter.Emit(compilation);

    /// <summary>从 <c>relation.plan.generated.json</c> 读取计划（解析失败 ⇒ false + 原因；禁静默给空计划）。</summary>
    public static bool TryBuildFromFile(
        string path, out RelationPlan plan, out IReadOnlyList<string> errors)
    {
        plan = RelationPlan.Empty;
        var problems = new List<string>();
        errors = problems;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            problems.Add($"关系计划文件不存在：{path}");
            return false;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null)
            {
                problems.Add($"关系计划非 JSON 对象：{path}");
                return false;
            }

            plan = Build(root);
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            problems.Add($"关系计划读取/解析失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>由 plan JSON 根对象构建中性 DTO（缺段 ⇒ 空表；段内缺字段 ⇒ 空串，交闸A/内核判）。</summary>
    public static RelationPlan Build(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var operations = new List<RelationOperation>();
        foreach (var node in Array(root, "operations"))
        {
            if (node is not JsonObject item) continue;
            operations.Add(new RelationOperation(
                Text(item, "id"),
                Nullable(item, "display"),
                Text(item, "edge"),
                Text(item, "src"),
                Text(item, "tgt"),
                Text(item, "flow"),
                Text(item, "namespace"),
                Strings(item, "exists"),
                Text(item, "duplicate"),
                Text(item, "onMissing"),
                Text(item, "arity"),
                Nullable(item, "emit")));
        }

        var invariants = new List<RelationInvariant>();
        foreach (var node in Array(root, "invariants"))
        {
            if (node is not JsonObject item) continue;
            invariants.Add(new RelationInvariant(
                Text(item, "id"), Text(item, "kind"), Text(item, "edge"),
                Text(item, "onViolation"), Nullable(item, "scope")));
        }

        var derivations = new List<RelationDerivation>();
        foreach (var node in Array(root, "derivations"))
        {
            if (node is not JsonObject item) continue;
            derivations.Add(new RelationDerivation(
                Text(item, "id"), Text(item, "from"), Text(item, "to"), Strings(item, "via"),
                Text(item, "op"), Text(item, "normalize"), Text(item, "order"), Strings(item, "empty")));
        }

        return new RelationPlan(
            Text(root, "version") is { Length: > 0 } version ? version : "1.0",
            operations, invariants, derivations);
    }

    private static IEnumerable<JsonNode?> Array(JsonObject root, string key)
        => root[key] is JsonArray array ? array : Enumerable.Empty<JsonNode?>();

    private static string Text(JsonObject item, string key)
        => item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static string? Nullable(JsonObject item, string key)
        => Text(item, key) is { Length: > 0 } text ? text : null;

    private static IReadOnlyList<string> Strings(JsonObject item, string key)
    {
        if (item[key] is not JsonArray array) return System.Array.Empty<string>();
        return array.OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var text) ? text : string.Empty)
            .Where(text => text.Length > 0)
            .ToList();
    }
}
