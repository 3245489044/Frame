#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Modules.Pipeline.StaticPipeline
{
    public delegate bool StepCanExecute<TContext>(TContext context);

    /// <summary>
    /// 兼容入口：执行体全部委托 <see cref="StaticPipelineKernel"/>（唯一循环 = BlockRunner）。
    /// </summary>
    internal static class PipelineExecutor
    {
        public static PipelineResult Execute<TContext>(
            IReadOnlyList<Step<TContext>> steps,
            TContext context,
            Action<TContext>? onBeforeStep,
            Action<TContext, string>? onAfterStep,
            Action<TContext, string, Exception>? onStepError,
            PipelineStopwatch? stopwatch = null)
            => StaticPipelineKernel.Execute(
                steps, context, onBeforeStep, onAfterStep, onStepError, stopwatch);

        public static Task<PipelineResult> ExecuteAsync<TContext>(
            IReadOnlyList<AsyncStep<TContext>> steps,
            TContext context,
            Func<TContext, Task>? onBeforeStep,
            Func<TContext, string, Task>? onAfterStep,
            Func<TContext, string, Exception, Task>? onStepError,
            PipelineStopwatch? stopwatch = null)
            => StaticPipelineKernel.ExecuteAsync(
                steps, context, onBeforeStep, onAfterStep, onStepError, stopwatch);
    }
}
