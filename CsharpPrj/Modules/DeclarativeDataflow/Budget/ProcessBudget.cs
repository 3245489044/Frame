using System;
using System.Collections.Generic;
using System.Linq;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Budget;

/// <summary>
/// 处理预算（不定深度的强制执法层）—— 对"不会自觉终止"的层次结构强制收口。
/// <para>
/// 信任反转原则：层结构允许自环（表达不定深度），因此<b>深度上限与节点总数上限不是可选项</b>，
/// 而是引擎对层次数据的强制边界。任一到界即截断为
/// <see cref="Processing.LevelProcessResult.Truncated"/>，不继续展开。
/// </para>
/// </summary>
/// <param name="MaxNodes">本次处理允许**处理**的最大节点数（含根；按实体去重后计数，被跳过的节点不重复计数）。</param>
/// <param name="MaxDepth">允许的最大路径深度（根 = 1；超过即截断）。</param>
public sealed record ProcessBudget(int MaxNodes, int MaxDepth)
{
    /// <summary>缺省预算（有限深度 + 有限节点；用于未显式配置时避免无界展开）。</summary>
    public static ProcessBudget Default { get; } = new(MaxNodes: 10_000, MaxDepth: 64);

    /// <summary>节点数是否越界（越界即截断）。</summary>
    public bool IsNodeLimitExceeded(int processedNodes) => processedNodes > MaxNodes;

    /// <summary>深度是否越界（越界即截断）。</summary>
    public bool IsDepthLimitExceeded(int depth) => depth > MaxDepth;
}
