#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicMethod.Block;

namespace Modules.Pipeline.StaticPipeline
{
    /// <summary>
    /// 静态管线内核适配：步骤 → Delegate 块，执行统一委托 <see cref="BlockRunner"/>。
    /// 不再保留第二套 foreach 执行循环。
    /// </summary>
    internal static class StaticPipelineKernel
    {
        public static PipelineResult Execute<TContext>(
            IReadOnlyList<Step<TContext>> steps,
            TContext context,
            Action<TContext>? onBeforeStep,
            Action<TContext, string>? onAfterStep,
            Action<TContext, string, Exception>? onStepError,
            PipelineStopwatch? stopwatch = null)
            => ExecuteAsync(steps, context, onBeforeStep, onAfterStep, onStepError, stopwatch)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

        public static async Task<PipelineResult> ExecuteAsync<TContext>(
            IReadOnlyList<Step<TContext>> steps,
            TContext context,
            Action<TContext>? onBeforeStep,
            Action<TContext, string>? onAfterStep,
            Action<TContext, string, Exception>? onStepError,
            PipelineStopwatch? stopwatch = null)
        {
            var blocks = new List<IBlock>(steps.Count);
            var names = new string[steps.Count];

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                names[i] = step.Name;
                blocks.Add(new StaticStepBlock<TContext>(
                    step, context, onBeforeStep, onAfterStep, onStepError));
            }

            return await RunAsync(blocks, names, stopwatch is not null).ConfigureAwait(false);
        }

        public static async Task<PipelineResult> ExecuteAsync<TContext>(
            IReadOnlyList<AsyncStep<TContext>> steps,
            TContext context,
            Func<TContext, Task>? onBeforeStep,
            Func<TContext, string, Task>? onAfterStep,
            Func<TContext, string, Exception, Task>? onStepError,
            PipelineStopwatch? stopwatch = null)
        {
            var blocks = new List<IBlock>(steps.Count);
            var names = new string[steps.Count];

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                names[i] = step.Name;
                blocks.Add(new StaticAsyncStepBlock<TContext>(
                    step, context, onBeforeStep, onAfterStep, onStepError));
            }

            return await RunAsync(blocks, names, stopwatch is not null).ConfigureAwait(false);
        }

        private static async Task<PipelineResult> RunAsync(
            IReadOnlyList<IBlock> blocks,
            string[] stepNames,
            bool enableTiming)
        {
            var blockContext = new BlockContext();
            var run = await BlockRunner.Shared
                .RunAsync(blocks, blockContext, CancellationToken.None)
                .ConfigureAwait(false);

            var timings = enableTiming ? BuildTimings(run, stepNames) : null;
            var errors = BuildErrors(run, stepNames);

            return run.Outcome switch
            {
                BlockRunOutcome.Success => PipelineResult.Success(timings),
                BlockRunOutcome.PartialSuccess => PipelineResult.PartialSuccess(errors, timings),
                BlockRunOutcome.Aborted => PipelineResult.Aborted(errors, timings),
                _ => PipelineResult.Failed(errors, timings)
            };
        }

        private static List<StepError> BuildErrors(BlockRunResult run, string[] stepNames)
        {
            var names = new HashSet<string>(stepNames, StringComparer.Ordinal);
            var errors = new List<StepError>();

            foreach (var diagnostic in run.Diagnostics)
            {
                if (diagnostic.Outcome != BlockOutcome.Failed || !names.Contains(diagnostic.BlockName))
                    continue;

                errors.Add(new StepError(
                    diagnostic.BlockName,
                    diagnostic.Exception
                    ?? new InvalidOperationException(diagnostic.Message ?? "步骤执行失败"),
                    diagnostic.Fatal));
            }

            return errors;
        }

        private static IReadOnlyList<StepTiming> BuildTimings(
            BlockRunResult run,
            string[] stepNames)
        {
            var byName = new Dictionary<string, BlockDiagnostic>(StringComparer.Ordinal);
            foreach (var diagnostic in run.Diagnostics)
                byName[diagnostic.BlockName] = diagnostic;

            var timings = new List<StepTiming>(stepNames.Length);
            foreach (var name in stepNames)
            {
                if (byName.TryGetValue(name, out var diagnostic))
                {
                    timings.Add(new StepTiming(
                        name,
                        diagnostic.Duration,
                        diagnostic.Success,
                        diagnostic.Outcome == BlockOutcome.Skipped));
                }
                else
                {
                    timings.Add(new StepTiming(name, TimeSpan.Zero, false));
                }
            }

            return timings;
        }
    }
}
