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
    /// <summary>种子键（与 Agent 侧 <c>TaskSeed.UserInputKey</c> 同字面；框架不持有域词，仅为数据键名）。</summary>
    private const string UserInputKey = "user_input";

    public static AxiomContext Create(IAgentLoopState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // S5：种子投影 —— 数据空间缺 user_input ⇒ 用状态根播种；两者皆空 ⇒ 显式失败（禁空串进骨架）。
        if (!state.Data.TryGet<string>(UserInputKey, out var dataValue) || string.IsNullOrWhiteSpace(dataValue))
        {
            if (string.IsNullOrWhiteSpace(state.UserInput))
                throw new InvalidOperationException("seed.source-missing: user_input");

            state.Data.Set(UserInputKey, state.UserInput);
        }

        return new(state.Data, state.Budget, state.Token, state.Flag)
        {
            Round = state.Round,
            LastEval = state.LastEval,
            Scope = state.Scope,
        };
    }
}
