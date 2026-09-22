using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Serialization;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 一次层次 schema 发射的产物：单根 <see cref="HierarchySchema"/> + 规范化 JSON + 发射期诊断。
/// <para>合成根（校验投影专用）**不出现**在任何产出中；多根 / 层图非法时不产出（诊断可见化，不静默兜底）。</para>
/// </summary>
public sealed record UsharpHierarchySchemaResult(
    HierarchySchema? Schema,
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    /// <summary>schema 已产出（且规范化 JSON 在场）。</summary>
    public bool Emitted => Schema is not null && Json is not null && !HasErrors;

    /// <summary>诊断单行报表（对齐 <c>UsharpFlowEmitResult.Report()</c> 风格）。</summary>
    public string Report()
        => Diagnostics.Count == 0
            ? "(no schema diagnostics)"
            : string.Join(Environment.NewLine, Diagnostics.Select(d => d.ToString()));
}

/// <summary>
/// <c>[Space] contains=</c> 图 → **单根** <see cref="HierarchySchema"/> 发射器（"层次是数据"的 Usharp 侧产出面）。
/// <para>
/// 映射：<c>LevelId</c>/<c>EntityType</c> ← 空间 Id；<c>ProcessId</c> ← <c>[Matrix]|[Flow] process=</c>；
/// <c>HierarchyContainment(child, edgeType)</c> ← 子空间的 <c>contains=</c>；根 = 唯一无父（且非自环）的划分空间。
/// </para>
/// <para>
/// 规范化器 = 内核 <see cref="HierarchySchemaJson.Serialize"/>（唯一实现，避免第二份 JSON 形态）。
/// 自环（<c>contains=自我:edge</c> = 递归划分）按内核口径原样保留。
/// </para>
/// </summary>
public static class UsharpHierarchySchemaEmitter
{
    /// <summary>发射本编译的层次 schema（层图非法 / 单根不成立 → 不产出 + error 诊断）。</summary>
    public static UsharpHierarchySchemaResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var declared = compilation.Spaces.Spaces.Where(s => s.Contains.Count > 0).ToList();
        if (declared.Count == 0)
            return new UsharpHierarchySchemaResult(null, null, diagnostics);

        // ① 包含对（子声明父）：形状 / 父层可解析不满足即不产出（闸A US1008 已在编译期报出）。
        var pairs = new List<(string Parent, string Child, string Edge)>();
        foreach (var space in declared)
            foreach (var containment in space.Contains)
            {
                if (containment.ParentSpaceId.Length == 0 || containment.EdgeType.Length == 0
                    || !compilation.Spaces.TryGet(containment.ParentSpaceId, out _))
                {
                    diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.SpaceHierarchyInvalid,
                        $"[Space] {space.SpaceId}: 划分声明 '{containment.ParentSpaceId}:{containment.EdgeType}' 不可产出"
                        + "（须为 <已声明父空间>:<包含边类型>）", space.SourceFile, space.LineNumber));
                    return new UsharpHierarchySchemaResult(null, null, diagnostics);
                }

                pairs.Add((containment.ParentSpaceId, space.SpaceId, containment.EdgeType));
            }

        // ② 层 Id 集 + 单根判定（自环不计入"有父"，对齐内核 LevelOrderFromRoot 口径）。
        var levelIds = new List<string>();
        var childIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (parent, child, _) in pairs)
        {
            if (!levelIds.Contains(parent, StringComparer.Ordinal)) levelIds.Add(parent);
            if (!levelIds.Contains(child, StringComparer.Ordinal)) levelIds.Add(child);
            if (!string.Equals(parent, child, StringComparison.Ordinal)) childIds.Add(child);
        }

        var roots = levelIds.Where(id => !childIds.Contains(id)).ToList();
        if (roots.Count != 1)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.SpaceHierarchyInvalid,
                $"空间划分{(roots.Count == 0 ? "无根" : "多根")}（根：{string.Join(", ", roots)}）"
                + "—— 内核 HierarchySchema 是单根模型，不产出",
                declared[0].SourceFile, declared[0].LineNumber));
            return new UsharpHierarchySchemaResult(null, null, diagnostics);
        }

        // ③ 层序 = 自根广度优先（含自环声明但自环不推进层序，避免自引用掩盖后续层）。
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(roots[0]);
        while (queue.Count > 0)
        {
            var levelId = queue.Dequeue();
            if (!seen.Add(levelId)) continue;
            ordered.Add(levelId);
            foreach (var (parent, child, _) in pairs)
            {
                if (!string.Equals(parent, levelId, StringComparison.Ordinal)) continue;
                if (string.Equals(child, levelId, StringComparison.Ordinal)) continue;
                if (!seen.Contains(child)) queue.Enqueue(child);
            }
        }

        var levels = ordered.Select(levelId => new HierarchyLevelSpec(
            levelId,
            levelId,
            pairs.Where(p => string.Equals(p.Parent, levelId, StringComparison.Ordinal))
                 .Select(p => new HierarchyContainment(p.Child, p.Edge))
                 .ToList(),
            ProcessIdOf(compilation, levelId))).ToList();

        var schema = new HierarchySchema(roots[0], levels);
        return new UsharpHierarchySchemaResult(schema, HierarchySchemaJson.Serialize(schema), diagnostics);
    }

    /// <summary>层处理器 Id（<c>[Matrix]|[Flow] process=</c> 的绑定；一层至多一个声明者由 US1011 保证）。</summary>
    private static string? ProcessIdOf(UsharpCompilation compilation, string levelId)
    {
        var fromMatrix = compilation.Matrices
            .FirstOrDefault(m => string.Equals(m.OverSpaceId, levelId, StringComparison.Ordinal))?.ProcessId;
        if (!string.IsNullOrWhiteSpace(fromMatrix)) return fromMatrix;

        var fromFlow = compilation.Flows
            .FirstOrDefault(f => string.Equals(f.OverSpaceId, levelId, StringComparison.Ordinal))?.ProcessId;
        return string.IsNullOrWhiteSpace(fromFlow) ? null : fromFlow;
    }
}
