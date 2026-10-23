using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 本轮种子键集来源（Agent 提供「值」，框架只做集合判据）：
/// 生成期由 Agent 以声明键表实现；运行期缺省回退 <c>ctx.Vars</c> 实际键集。
/// </summary>
public interface IRoundSeedKeySource
{
    IReadOnlyCollection<string> SeedKeys(RoundSpec spec, NodeRoundContext ctx);
}

/// <summary>原始返回值 → 三态解码端口（Agent 侧实现错误信封识别；未命中回退现有反射提取，零回归）。</summary>
public interface IFunctionOutcomeDecoder
{
    bool TryDecode(object? raw, out FunctionOutcome outcome);
}
