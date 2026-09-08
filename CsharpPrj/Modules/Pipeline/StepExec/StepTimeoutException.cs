using System;
using Modules.Threading;

namespace Modules.Pipeline.StepExec
{
    /// <summary>
    /// 单步执行超时异常（I3 / S3 治理）：某一步骤（读源 / handler / return_spec 全程）在单步预算内未完成。
    /// 以**非 OperationCanceledException** 的强类型抛出，使编排器将其归集为「步骤错误」（ctx.Error + OnStepErrorAsync）
    /// 而非「整轮取消」——避免单步卡死被误判为外层取消而静默停整循环。语义对齐 round-timeout 的 TO 转归：
    /// 承载 funcName + timeout，供诊断日志直接自证卡点。
    /// 实现 <see cref="IBudgetExhausted"/> 标记：统一 ErrorClassifier 据此归为 Timeout（而非误归 ExecutionFailure）。
    /// </summary>
    public sealed class StepTimeoutException : Exception, IBudgetExhausted
    {
        public StepTimeoutException(string function, TimeSpan timeout)
            : base($"单步超时: function={function} 未在 {timeout.TotalSeconds:0.#}s 内完成（含入参读源）")
        {
            Function = function;
            Timeout = timeout;
        }

        /// <summary>卡死步骤函数名（如 rule_match）。</summary>
        public string Function { get; }

        /// <summary>单步预算。</summary>
        public TimeSpan Timeout { get; }
    }
}