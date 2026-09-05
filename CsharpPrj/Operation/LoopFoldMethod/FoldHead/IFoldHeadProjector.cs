using System.Collections.Generic;

namespace Operation.LoopFoldMethod.FoldHead;

/// <summary>
/// 折叠投影：把已展开前缀收敛成「当前等效判定点」（折叠头 FoldHead）—— 旧循环引擎缺失的抽象，
/// 旧引擎只回「最终状态」，本投影把状态带折叠成此刻的判定点。
/// TState 同时被读（trace 输入）与产出（Head 返回），故为不变方差。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public interface IFoldHeadProjector<TState>
{
    /// <summary>折叠已展开前缀 → 当前判定点。</summary>
    /// <param name="trace">只增状态带（含初始点 s₀）。</param>
    TState Head(IReadOnlyList<TState> trace);
}
