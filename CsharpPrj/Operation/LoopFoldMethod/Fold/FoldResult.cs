using System;
using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Fold;

/// <summary>
/// 折叠结论：FoldHead + FoldTrace + 每步顶点 + 坐标 + 耗时 + FoldCut —— 整条带的收敛封装。
/// 注：设计文档签名 <c>FoldResult&lt;TState&gt;</c> 需同时携带每步算子结论（TVertex），故本实现为
/// <c>FoldResult&lt;TState, TVertex&gt;</c>（两泛型），语义等价、显式更完整。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed record FoldResult<TState, TVertex>(
    /// <summary>折叠头 —— 已展开前缀收敛成的「当前等效判定点」。</summary>
    TState Head,

    /// <summary>只增状态带（点）。</summary>
    FoldTrace<TState> Trace,

    /// <summary>每步算子结论（顶点，含坐标）。</summary>
    IReadOnlyList<FoldVertex<TVertex>> Vertices,

    /// <summary>已完成的展开步数（坐标）。</summary>
    int Coordinate,

    /// <summary>折叠总耗时。</summary>
    TimeSpan TotalDuration,

    /// <summary>截断值（终值 = 折叠如何结束）。</summary>
    FoldCut Cut);
