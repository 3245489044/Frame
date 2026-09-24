using System;
using System.Collections.Generic;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.LoopFoldMethod.Reference.Projection;

/// <summary>
/// 折叠头投影在空间维度的默认实现（RefHead）—— 把累积历史（History→Task→Node 归拢的节点）折叠成
/// 「此刻的判定视图」。思想对齐 <c>IFoldHeadProjector.Head(trace)</c>（前缀收敛 → 当前判定点），作用于引用图而非状态带。
/// <para>
/// 折叠规则（确定性、LLM-free）：按<b>键内序数</b>排序 —— 时序权威 = 键内 <c>(round, seq)</c>
/// （<see cref="SessionMessageKeys.OrderComparer"/>：轮次 → 轮内 seq → 键名字典序兜底），
/// 不再依赖 <c>round_sequence</c> 引用边（A4′：该边已退役；「删除全部边后顺序不变」即本判定式的成立条件）。
/// 调用方可经 <see cref="RefHeadContext.KeyOrder"/> 覆写键序（业务节点族自有序数约定时）。
/// </para>
/// <para>
/// 本默认投影**只读**输入（<see cref="RefHeadContext"/>），不写边（写边唯一 owner 在 DataStoreDriven）。
/// LLM 丰富分层提示词视图由 Agent/Planner 实现 <see cref="IRefProjector{TContext}"/> 提供，不在本层。
/// </para>
/// </summary>
public sealed class RefHead : IRefProjector<RefHeadContext>
{
    /// <summary>共享默认实例（无状态纯投影）。</summary>
    public static readonly RefHead Default = new();

    /// <inheritdoc />
    public IRefView Project(RefHeadContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var nodeKeys = ctx.NodeKeys ?? Array.Empty<string>();
        var order = ctx.KeyOrder ?? SessionMessageKeys.OrderComparer;

        // 键序即时序：稳定排序（比较相等则保持归拢原序）——无 IO、无图查，删边后顺序不变。
        var sequence = nodeKeys.OrderBy(key => key, order).ToList();

        return new RefView(ctx.ScopeKey, nodeKeys.ToList(), sequence);
    }
}

