using System;

namespace Modules.Threading;

/// <summary>
/// 执行边界预算耗尽标记（预算终止 = 引擎内可判定，区别于协作式取消）。
/// 由更高层（Operation.HierarchyMethod.Center 边界异常族 / 单步超时异常等）实现；分类器据此归为
/// <see cref="Share.TaskStatusPool.Loops.ErrorClass.Timeout"/>，避免「有界窗口到点」被默认误归为 ExecutionFailure。
/// <para>
/// 原声明于 Share.TaskStatusPool.Loops；StepExec 走廊（Modules.Pipeline）需实现本标记，而 TaskStatusPool 已反向依赖
/// Modules.Pipeline（成环），故下沉到本叶子层（Modules.Threading，无工程反向依赖）以维持依赖单向。
/// </para>
/// </summary>
public interface IBudgetExhausted
{
}