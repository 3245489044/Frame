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

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics, "(no schema diagnostics)");
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
    /// <summary>发射本编译的层次 schema（层图非法 / 单根不成立 / 一层多处理器 → 不产出 + error 诊断）。</summary>
    public static UsharpHierarchySchemaResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var graph = UsharpHierarchyGraph.Build(compilation.Spaces);
        if (graph is null) return new UsharpHierarchySchemaResult(null, null, diagnostics);

        var attribution = graph.DeclaredSpaces[0];

        // ① 形状非法 ⇒ 不产出（域内判据码；不复读闸A 的 US1008）
        if (graph.InvalidDeclarations.Count > 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.HierarchySchemaNotProducible,
                $"层次 schema 不可产出：划分声明形状非法（{string.Join("；", graph.InvalidDeclarations)}）"
                + "—— 须为 <已声明父空间>:<包含边类型>",
                attribution.SourceFile, attribution.LineNumber));
            return new UsharpHierarchySchemaResult(null, null, diagnostics);
        }

        // ② 一层多处理器绑定 ⇒ 不产出（判定单源 = UsharpProcessBindings；静默择一属「错误状态下仍产出」）
        var multiBound = UsharpProcessBindings.MultiBoundLayers(
            compilation.Matrices.Where(m => m.ProcessId is not null).Select(m => (m.OverSpaceId, m.ProcessId))
                .Concat(compilation.Flows.Where(f => f.ProcessId is not null).Select(f => (f.OverSpaceId, f.ProcessId))));
        if (multiBound.Count > 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.HierarchySchemaNotProducible,
                $"层次 schema 不可产出：层 {string.Join("/", multiBound)} 的处理代数重复声明（一层恰一个处理器声明者）",
                attribution.SourceFile, attribution.LineNumber));
            return new UsharpHierarchySchemaResult(null, null, diagnostics);
        }

        // ③ 单根判定（内核 HierarchySchema 是单根模型）
        if (!graph.IsSingleRoot)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.HierarchySchemaNotProducible,
                $"层次 schema 不可产出：空间划分{(graph.Roots.Count == 0 ? "无根" : "多根")}"
                + $"（根：{string.Join(", ", graph.Roots)}）—— 内核 HierarchySchema 是单根模型",
                attribution.SourceFile, attribution.LineNumber));
            return new UsharpHierarchySchemaResult(null, null, diagnostics);
        }

        var root = graph.Roots[0];
        var levels = graph.LevelOrder(root).Select(levelId => new HierarchyLevelSpec(
            levelId,
            levelId,
            graph.ChildrenOf(levelId).Select(c => new HierarchyContainment(c.Child, c.Edge)).ToList(),
            ProcessIdOf(compilation, levelId))).ToList();

        var schema = new HierarchySchema(root, levels);
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
