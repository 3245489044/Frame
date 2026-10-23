using Operation.HierarchyMethod.Tasks.Loops;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Operation.HierarchyMethod.Nodes;

/// <summary>
/// 执行租约工厂 —— 从循环状态根（<see cref="IAgentLoopState"/>）投影每轮 <see cref="AxiomContext"/> 执行租约。
/// 状态根可为会话条目或独立循环快照，二者均实现 <see cref="IAgentLoopState"/>。
/// 语义：共享 Data（单一数据持有方 = 循环状态根）+ Budget/Token/Flag 同实例引用（单写无漂移），
/// 仅标量 Round/LastEval/Scope 从根播种。
/// <para>自 Agent 侧同类型平移（两侧均框架类型：<see cref="IAgentLoopState"/> + <see cref="AxiomContext"/>）。</para>
/// </summary>
public static class LeaseContextFactory
{
    public static AxiomContext Create(IAgentLoopState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // S5：种子投影 —— 唯一解析实现（SeedInput：input → UserInput → 兼容键 user_input）；
        // 三者皆空 ⇒ 显式失败（禁空串进骨架）。续跑轮（无新输入）由此读到跨轮既有输入。
        var effective = SeedInput.Require(state.Data, state.UserInput);

        // 种子物化：input 缺失时补写（派生侧只认该键，不再重复推断优先级）。
        if (!state.Data.TryGet<string>(SeedInputKeys.Input, out var seeded) || string.IsNullOrWhiteSpace(seeded))
            state.Data.Set(SeedInputKeys.Input, effective);

        return new(state.Data, state.Budget, state.Token, state.Flag)
        {
            Round = state.Round,
            LastEval = state.LastEval,
            Scope = state.Scope,
            // D08：框架工厂补齐 UserInput（旧实现从不写 ⇒ LeaseDeriver 的 root.UserInput 回退是死分支）。
            UserInput = effective,
        };
    }
}
