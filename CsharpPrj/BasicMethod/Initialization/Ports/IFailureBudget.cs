using BasicMethod.Initialization.Errors;

namespace BasicMethod.Initialization.Ports;

/// <summary>
/// 失败账本端口（axioms §2 FailureBudget / §5 FailureBudgetEscalation）。
/// 抽象上提至本层，实现在下游（如 Share.TaskStatusPool.Loops.FailureBudget + BudgetEscalator），经组合根注入——
/// 使记账成为「唯一入口」，消除全库散落的 catch→log→continue。
/// </summary>
public interface IFailureBudget
{
    /// <summary>按容忍度入账：tolerated 只记账（不升 level）；fatal 升级熔断等级。</summary>
    void Apply(ErrorTolerance tolerance);
}
