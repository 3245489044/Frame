using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Fold;

/// <summary>
/// 只增状态带（质点展开的轨迹，框架的既成历史）。写时追加、不覆盖已有点 —— 信息只增不回写
/// （对齐 AX-CORE.InformationMonotonicity）。折叠头 <see cref="FoldHead.IFoldHeadProjector{TState}"/> 据此投影判定点。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class FoldTrace<TState>
{
    private readonly List<TState> _states;

    public FoldTrace() => _states = new List<TState>();

    public FoldTrace(IEnumerable<TState> states) => _states = new List<TState>(states);

    /// <summary>已展开的状态点数量（含初始点 s₀）。</summary>
    public int Count => _states.Count;

    /// <summary>当前最后一个状态点（空带回退 default）。</summary>
    public TState? Last => _states.Count > 0 ? _states[^1] : default;

    /// <summary>追加一个状态点（只增带，不覆盖既有）。</summary>
    public void Append(TState state) => _states.Add(state);

    /// <summary>只读快照（供折叠头投影 / 观测 / 消费方读取）。</summary>
    public IReadOnlyList<TState> AsReadOnly() => _states;
}
