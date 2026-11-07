#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// <see cref="ResolvedLayer"/> → <see cref="ResolvedLayerSpec"/> 的唯一适配入口。
/// <para>
/// 两家载体（WinForms 绘制 / HTML CSS 投影）都从这里取同一份解析结果，禁止各自再读
/// <c>layer.Params</c>；<see cref="LayerIssue"/> 在此统一转成既有 <see cref="SurfaceIssue"/> 口径
/// （稳定码与既有断言/日志一致，不改名）。
/// </para>
/// </summary>
public static class ResolvedLayerSpecProjection
{
    /// <summary>解析一层。</summary>
    /// <param name="layer">已解析层。</param>
    /// <param name="issues">产出问题（无则空）。</param>
    public static ResolvedLayerSpec Parse(ResolvedLayer layer, out IReadOnlyList<SurfaceIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var spec = ResolvedLayerSpec.Parse(ToInput(layer), out var layerIssues);
        issues = ToSurfaceIssues(layerIssues);
        return spec;
    }

    /// <summary>
    /// 解析结果缓存（K1「解析期一次」）：同一层栈对象只解析一次，后续绘制/投影直接复用。
    /// <para>键为层栈对象本身（弱引用，材质回收即释放）；层栈不可变 ⇒ 结果可安全共享。</para>
    /// </summary>
    private static readonly ConditionalWeakTable<IReadOnlyList<ResolvedLayer>, ResolvedLayerSpecSet> Parsed =
        new();

    /// <summary>解析整组层（同一层栈只解析一次；首次调用产出问题，后续命中缓存）。</summary>
    /// <param name="layers">材质层（可为空）。</param>
    public static ResolvedLayerSpecSet ParseCached(IReadOnlyList<ResolvedLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return Parsed.GetValue(layers, Parse);
    }

    /// <summary>一次解析整组层（顺序即入参顺序），并给出材质级最大声明半径。</summary>
    /// <param name="layers">材质层（可为空）。</param>
    public static ResolvedLayerSpecSet Parse(IReadOnlyList<ResolvedLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0) return ResolvedLayerSpecSet.Empty;

        var specs = new ResolvedLayerSpec[layers.Count];
        var issues = new List<SurfaceIssue>();
        var materialRadius = 0f;
        for (var i = 0; i < layers.Count; i++)
        {
            var spec = ResolvedLayerSpec.Parse(ToInput(layers[i]), out var layerIssues);
            specs[i] = spec;
            foreach (var issue in layerIssues) issues.Add(new SurfaceIssue(issue.Code, issue.Detail));
            if (spec.RadiusDeclared > materialRadius) materialRadius = spec.RadiusDeclared;
        }

        return new ResolvedLayerSpecSet(specs, materialRadius, issues);
    }

    /// <summary>层最小只读视图（搬运，不做语义解释）。</summary>
    /// <param name="layer">已解析层。</param>
    public static BorderLayerInput ToInput(ResolvedLayer layer)
        => new(layer.Role, layer.Primitive, layer.Z, layer.Params);

    private static IReadOnlyList<SurfaceIssue> ToSurfaceIssues(IReadOnlyList<LayerIssue> issues)
    {
        if (issues.Count == 0) return Array.Empty<SurfaceIssue>();
        var mapped = new SurfaceIssue[issues.Count];
        for (var i = 0; i < issues.Count; i++) mapped[i] = new SurfaceIssue(issues[i].Code, issues[i].Detail);
        return mapped;
    }
}

/// <summary>整组层的解析产物（一次解析、两家载体共用）。</summary>
/// <param name="Specs">逐层规格（顺序 = 入参顺序）。</param>
/// <param name="MaterialRadius">材质级最大声明半径（DIP；0 = 未声明）。</param>
/// <param name="Issues">解析问题汇总（去重由调用方按语义决定）。</param>
public sealed record ResolvedLayerSpecSet(
    IReadOnlyList<ResolvedLayerSpec> Specs, float MaterialRadius, IReadOnlyList<SurfaceIssue> Issues)
{
    /// <summary>空集合。</summary>
    public static readonly ResolvedLayerSpecSet Empty = new(
        Array.Empty<ResolvedLayerSpec>(), 0f, Array.Empty<SurfaceIssue>());
}
