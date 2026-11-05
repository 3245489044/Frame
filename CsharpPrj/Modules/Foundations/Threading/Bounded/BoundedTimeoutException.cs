using System;

namespace Modules.Threading
{
    /// <summary>有界执行耗尽类型（阻塞归属）：step=步骤级 / round=整轮级 / store=存储写读门。</summary>
    public enum BoundedTimeoutKind
    {
        Step = 0,
        Round = 1,
        Store = 2
    }

    /// <summary>
    /// 有界执行窗口耗尽自证异常（框架自有，模块无外部依赖故不引用业务异常族）。
    /// 携带阻塞归属（Kind + Subject）与命中的 Budget，供日志/编排器直接自证阻塞点；**非 OperationCanceledException**，
    /// 调用方据此按『该级预算耗尽』归因（如步骤级→步骤错误 / 轮级→终止决策 / 存储级→写门卡点），而非误判整轮取消。
    /// 业务侧适配层（如 Operation.HierarchyMethod.Center）可将本异常映射为其强类型 ExecutionBoundaryException 族。
    /// </summary>
    public sealed class BoundedTimeoutException : Exception
    {
        public BoundedTimeoutException(BoundedTimeoutKind kind, string subject, TimeSpan budget)
            : base($"[bounded {KindName(kind)}/{subject} budget={(long)budget.TotalSeconds}s] bounded execution exceeded budget")
        {
            Kind = kind;
            Subject = subject;
            Budget = budget;
        }

        public BoundedTimeoutKind Kind { get; }

        public string Subject { get; }

        public TimeSpan Budget { get; }

        private static string KindName(BoundedTimeoutKind kind) => kind switch
        {
            BoundedTimeoutKind.Step => "step",
            BoundedTimeoutKind.Round => "round",
            _ => "store"
        };
    }
}
