using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 层处理器绑定（<c>process=</c>）的**判定单源**：``[Matrix]|[Flow] process=`` 的「一层恰一个声明者」条件，
/// 由闸A（US1011）与产出面（schema 发射器「非法即不产出」）共用，禁止各自复写。
/// </summary>
public static class UsharpProcessBindings
{
    /// <summary>
    /// 本编译全部 <c>process=</c> 绑定（<c>[Matrix]</c> 与 <c>[Flow]</c> 合并，保声明序；<c>process=</c> 为空者不参与）。
    /// <para>投影单源：发射面（schema 发射器）经此取绑定，不再各处复制 <c>Where/Select/Concat</c>。</para>
    /// </summary>
    /// <param name="compilation">编译结果。</param>
    public static IReadOnlyList<(string? Layer, string? ProcessId)> BindingsOf(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return BindingsOf(compilation.Matrices, compilation.Flows);
    }

    /// <summary>声明集合 → 全部 <c>process=</c> 绑定（保声明序）。</summary>
    /// <param name="matrices">矩阵声明。</param>
    /// <param name="flows">流声明。</param>
    public static IReadOnlyList<(string? Layer, string? ProcessId)> BindingsOf(
        IEnumerable<UsharpMatrixDecl> matrices, IEnumerable<UsharpFlowDecl> flows)
    {
        ArgumentNullException.ThrowIfNull(matrices);
        ArgumentNullException.ThrowIfNull(flows);

        var bindings = new List<(string? Layer, string? ProcessId)>();
        foreach (var matrix in matrices)
        {
            if (matrix.ProcessId is not null) bindings.Add((matrix.OverSpaceId, matrix.ProcessId));
        }

        foreach (var flow in flows)
        {
            if (flow.ProcessId is not null) bindings.Add((flow.OverSpaceId, flow.ProcessId));
        }

        return bindings;
    }

    /// <summary>
    /// 同层出现 ≥2 个 <c>process=</c> 声明者（**在场即声明**，空值亦计入 ⇒ 框架侧必静默覆盖）的层集（保序去重）。
    /// <paramref name="bindings"/> 的层 Id 为空白者不参与（其自身形状问题由闸A 单报）。
    /// </summary>
    public static IReadOnlyList<string> MultiBoundLayers(IEnumerable<(string? Layer, string? ProcessId)> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (layer, processId) in bindings)
        {
            if (processId is null || string.IsNullOrWhiteSpace(layer)) continue;
            owners[layer] = owners.TryGetValue(layer, out var n) ? n + 1 : 1;
        }

        return owners.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();
    }

    /// <summary>
    /// 取某层的处理器 Id：该层全部声明者中**唯一的非空 <c>process=</c>**。
    /// <para>
    /// 顺序假设消除（E-5）：不再 `FirstOrDefault(over==level)?.ProcessId`；
    /// 多个非空声明者 ⇒ 返回 null（由闸A US1011 报，产物不猜）。
    /// </para>
    /// </summary>
    /// <param name="bindings">全部绑定（层, process=）。</param>
    /// <param name="layerId">层 Id。</param>
    public static string? ProcessOf(
        IEnumerable<(string? Layer, string? ProcessId)> bindings, string? layerId)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (string.IsNullOrWhiteSpace(layerId)) return null;

        string? found = null;
        foreach (var (layer, processId) in bindings)
        {
            if (string.IsNullOrWhiteSpace(processId)) continue;
            if (!string.Equals(layer, layerId, StringComparison.Ordinal)) continue;
            if (found is not null) return null; // 多声明者 ⇒ 冲突，由 US1011 报
            found = processId;
        }

        return found;
    }
}
