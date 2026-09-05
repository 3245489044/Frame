using System.Collections.Generic;
using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Operator;

/// <summary>
/// 折叠上下文：坐标 + 预算 + 前置顶点（供外部算子判断当前位置与已展开历史）。
/// 折叠器每轮构造并注入算子；算子只读、不修改（借出控制权的宿主记账面）。
/// </summary>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed record FoldContext<TVertex>(
    /// <summary>当前坐标（已完成的展开步数）。</summary>
    int Coordinate,

    /// <summary>折叠预算（坐标上限，执法）。</summary>
    FoldBudget Budget,

    /// <summary>前置顶点（已展开各步的算子结论，只读历史）。</summary>
    IReadOnlyList<FoldVertex<TVertex>> PreviousVertices);
