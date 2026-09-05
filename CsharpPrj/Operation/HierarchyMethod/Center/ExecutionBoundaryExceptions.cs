using System;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// 执行边界自证异常族基类（Center 唯一 canonical）。
/// 贯穿「引擎 → 编排 → 步骤 → 存储」同一取消/预算语义的强类型结果：在 <see cref="ExecutionScope"/> 某级有界窗口
/// 耗尽（而非外层取消）时抛出，携带阻塞归属 (Stage/Step) 与命中的预算（Budget），使日志/编排器能直接自证阻塞点、
/// 按语义归因——不依赖调用方再从堆栈逐层析构。非 OperationCanceledException：编排器据此按「该级预算耗尽」收敛
/// （如步骤级→步骤错误 / 轮级→TO），而非误判整轮取消。
/// </summary>
public abstract class ExecutionBoundaryException : Exception, IBudgetExhausted
{
    protected ExecutionBoundaryException(
        string stage, string? step, TimeSpan budget, string detail)
        : base(detail)
    {
        Stage = stage;
        Step = step;
        Budget = budget;
    }

    /// <summary>阻塞归属阶段（step / round / store），配合堆栈顶一次定属阻塞点。</summary>
    public string Stage { get; }

    /// <summary>阻塞归属对象（函数名 / 节点键 / 写读对象）；null = 轮级/根级。</summary>
    public string? Step { get; }

    /// <summary>命中的预算窗口。</summary>
    public TimeSpan Budget { get; }

    /// <summary>自证消息统一加 Stage/Step/Budget 前缀：任何日志无需再析异常即可定位。</summary>
    public override string Message =>
        $"[execution-boundary {Stage}{(string.IsNullOrEmpty(Step) ? string.Empty : $"/{Step}")} budget={(long)Budget.TotalSeconds}s] {base.Message}";
}

/// <summary>步骤级有界窗口耗尽（确定性读源 / 函数单步 await 卡死）。编排器按「步骤错误」归集，不翻转整轮业务 ok。</summary>
public sealed class StepTimeoutException : ExecutionBoundaryException
{
    public StepTimeoutException(string step, TimeSpan budget, string stage = "step")
        : base(stage, step, budget, "bounded step execution exceeded budget") { }
}

/// <summary>整轮（根）预算耗尽。编排器收敛为终止决策（对齐预算终止 TO 引擎内强制的语义，阈值到达即触发、不外置）。</summary>
public sealed class RoundTimeoutException : ExecutionBoundaryException
{
    public RoundTimeoutException(string stage, TimeSpan budget)
        : base(stage, step: null, budget, "round budget exhausted") { }
}

/// <summary>存储有界等待耗尽（写门 / 读源 / Outbox 落库的 SemaphoreSlim + CancelAfter 窗口）。携带写读对象以自证卡点介质。</summary>
public sealed class StoreTimeoutException : ExecutionBoundaryException
{
    public StoreTimeoutException(string target, TimeSpan budget, string stage = "store")
        : base(stage, target, budget, "bounded store operation exceeded budget") { }
}
