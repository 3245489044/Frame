using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 一次位置反解发射的产物：<c>loc → 位置 id 集</c> 规范化 JSON + 发射期诊断。
/// <para>矩阵缺失 / 行形状非法 ⇒ **不产出**（诊断可见化，不静默兜底）。</para>
/// </summary>
public sealed record UsharpLocPlacementResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    /// <summary>诊断单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics, "(no loc-placement diagnostics)");
}

/// <summary>
/// 位置反解声明 → <c>loc → 位置 id 集</c> 映射（U12 L2：位置 = loc 的反解产物，不手写声明）。
/// <para>
/// 输入形态（唯一可行形态，实测锁定见 <c>Modules/Node/docs/节点通用框架-表层语言与Usharp接入.md</c> §14）：
/// <c>[Space] LocPlacement</c>（loc 值 ∪ 位置 id 同处一键空间）+ <c>[Matrix] LocToPlacement: over=LocPlacement</c>，
/// 行 = <c>⟨位置 id = loc 值⟩</c>。
/// </para>
/// <para>
/// 产出**中立 JSON**（BCL 文本，无领域类型）：<c>{"map":{"&lt;loc&gt;":["&lt;位置 id&gt;",…]}}</c> ——
/// 位置层（<c>Modules.Node</c>）经既有 <c>IPlacementResolver</c> 端口消费，本发射器不引位置层类型
/// （耦合方向：禁 <c>Modules.Node → Usharp</c>；桥接类型只在适配工程）。
/// </para>
/// <para>
/// 命名归一（**唯一映射点**）：坐标名禁 <c>-</c>（<see cref="UsharpIdentifier"/>：与线性表达式减号同形）
/// ⇒ 声明面用下划线形（<c>archive_raw</c>），产出前归一为位置 id 的 kebab 形（<c>archive-raw</c>）。
/// 归一为单射：C 侧守卫 <c>Check-NodeLocPlacement</c> 以「位置 id 集（域声明单源）」双向比对覆盖完整性。
/// </para>
/// </summary>
public static class UsharpLocPlacementEmitter
{
    /// <summary>反解矩阵的声明名（唯一权威；禁在别处再写字面量）。</summary>
    public const string MatrixId = "LocToPlacement";

    /// <summary>反解域键空间名（唯一权威）。</summary>
    public const string SpaceId = "LocPlacement";

    /// <summary>loc 受控词集空间名（唯一权威）。</summary>
    public const string LocSpaceId = "Loc";

    /// <summary>发射本编译的位置反解表（矩阵缺失 / 行输出非法 / loc 未声明 ⇒ 不产出 + error）。</summary>
    public static UsharpLocPlacementResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var matrix = compilation.Matrices.FirstOrDefault(
            m => string.Equals(m.MatrixId, MatrixId, StringComparison.Ordinal));

        if (matrix is null)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.LocPlacementNotProducible,
                $"位置反解表不可产出：未找到 [Matrix] {MatrixId}（位置 = loc 反解的唯一声明面）",
                SpaceId, 0));
            return new UsharpLocPlacementResult(null, diagnostics);
        }

        if (!compilation.Spaces.TryGet(SpaceId, out var domainSpace))
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.LocPlacementNotProducible,
                $"位置反解表不可产出：未找到 [Space] {SpaceId}（反解域键空间）",
                matrix.SourceFile, matrix.LineNumber));
            return new UsharpLocPlacementResult(null, diagnostics);
        }

        if (!compilation.Spaces.TryGet(LocSpaceId, out var locSpace))
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.LocPlacementNotProducible,
                $"位置反解表不可产出：未找到 [Space] {LocSpaceId}（loc 受控词集单源）",
                matrix.SourceFile, matrix.LineNumber));
            return new UsharpLocPlacementResult(null, diagnostics);
        }

        var locs = locSpace.Axes.Select(a => a.Name).ToArray();
        var domainCoords = new HashSet<string>(domainSpace.Axes.Select(a => a.Name), StringComparer.Ordinal);

        // loc → 位置 id 集（序 = 声明序；同一 loc 多行 = 该 loc 的多个落点）
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in matrix.Rows)
        {
            if (!domainCoords.Contains(row.OutputKey))
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.LocPlacementNotProducible,
                    $"位置反解表不可产出：行输出 '{row.OutputKey}' 不在 [Space] {SpaceId} 声明内（US1003 同族）",
                    matrix.SourceFile, matrix.LineNumber));
                continue;
            }

            var loc = row.Expression.Trim();
            if (!locs.Contains(loc, StringComparer.Ordinal))
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.LocPlacementNotProducible,
                    $"位置反解表不可产出：行 '{row.OutputKey}' 引用了非 loc 词表项 '{loc}'"
                    + $"（loc 单源 = [Space] {LocSpaceId}）",
                    matrix.SourceFile, matrix.LineNumber));
                continue;
            }

            if (!map.TryGetValue(loc, out var placements))
            {
                map[loc] = placements = new List<string>();
            }

            placements.Add(ToRuntimeId(row.OutputKey));
        }

        if (diagnostics.Any(d => d.Severity == UsharpSeverity.Error))
        {
            return new UsharpLocPlacementResult(null, diagnostics);
        }

        // 映射键 = loc **值**（运行期 IPlacementResolver.Resolve(loc, …) 按值查表）⇒ 键亦须归一
        var normalized = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in map)
        {
            normalized[ToRuntimeId(pair.Key)] = pair.Value;
        }

        return new UsharpLocPlacementResult(Serialize(normalized), diagnostics);
    }

    /// <summary>规范化 JSON（<c>JsonObject</c> 保插入序 ⇒ 序 = 声明序，等价门可比对）。</summary>
    private static string Serialize(IReadOnlyDictionary<string, List<string>> map)
    {
        var root = new JsonObject();
        foreach (var pair in map)
        {
            var placements = new JsonArray();
            foreach (var placementId in pair.Value)
            {
                placements.Add(placementId);
            }

            root[pair.Key] = placements;
        }

        return root.ToJsonString();
    }

    /// <summary>
    /// 声明面坐标名 → 运行期标识（下划线 → kebab；**唯一映射点**，两侧同规则）：
    /// 行输出 = 位置 id（<c>archive_raw → archive-raw</c>）；行输入 = loc 值（<c>agent_round → agent-round</c>）。
    /// </summary>
    private static string ToRuntimeId(string coordinate)
        => coordinate.Replace('_', '-');
}
