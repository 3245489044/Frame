

#nullable enable
using System;
using System.Collections.Generic;

namespace Modules.Pipeline.StaticPipeline
{

    public delegate bool StepCanExecute<TContext>(TContext context);


    internal static class PipelineExecutor
    {
        public static PipelineResult Execute<TContext>(
            IReadOnlyList<Step<TContext>> steps,
            TContext context,
            Action<TContext>? onBeforeStep,
            Action<TContext, string>? onAfterStep,
            Action<TContext, string, Exception>? onStepError,
            PipelineStopwatch? stopwatch = null)
        {
            var errors = new List<StepError>();
            stopwatch?.Start();

            foreach (var step in steps)
            {
                if (step.CanExecute != null && !step.CanExecute(context))
                {
                    stopwatch?.Record(step.Name, isSuccess: true, isSkipped: true);
                    continue;
                }

                try
                {
                    onBeforeStep?.Invoke(context);
                    step.Execute(context);
                    onAfterStep?.Invoke(context, step.Name);
                    stopwatch?.Record(step.Name, isSuccess: true);
                }
                catch (Exception ex) when (step.IsFatal)
                {
                    var stepError = new StepError(step.Name, ex, isFatal: true);
                    errors.Add(stepError);
                    onStepError?.Invoke(context, step.Name, ex);
                    stopwatch?.Record(step.Name, isSuccess: false);
                    return PipelineResult.Failed(errors, stopwatch?.Timings);
                }
                catch (Exception ex)
                {
                    var stepError = new StepError(step.Name, ex, isFatal: false);
                    errors.Add(stepError);
                    onStepError?.Invoke(context, step.Name, ex);
                    stopwatch?.Record(step.Name, isSuccess: false);
                }
            }

            return errors.Count > 0
                ? PipelineResult.PartialSuccess(errors, stopwatch?.Timings)
                : PipelineResult.Success(stopwatch?.Timings);
        }

        public static async System.Threading.Tasks.Task<PipelineResult> ExecuteAsync<TContext>(
            IReadOnlyList<AsyncStep<TContext>> steps,
            TContext context,
            Func<TContext, System.Threading.Tasks.Task>? onBeforeStep,
            Func<TContext, string, System.Threading.Tasks.Task>? onAfterStep,
            Func<TContext, string, Exception, System.Threading.Tasks.Task>? onStepError,
            PipelineStopwatch? stopwatch = null)
        {
            var errors = new List<StepError>();
            stopwatch?.Start();

            foreach (var step in steps)
            {
                if (step.CanExecute != null && !step.CanExecute(context))
                {
                    stopwatch?.Record(step.Name, isSuccess: true, isSkipped: true);
                    continue;
                }

                try
                {
                    if (onBeforeStep != null)
                        await onBeforeStep(context).ConfigureAwait(false);

                    await step.Execute(context).ConfigureAwait(false);

                    if (onAfterStep != null)
                        await onAfterStep(context, step.Name).ConfigureAwait(false);

                    stopwatch?.Record(step.Name, isSuccess: true);
                }
                catch (Exception ex) when (step.IsFatal)
                {
                    var stepError = new StepError(step.Name, ex, isFatal: true);
                    errors.Add(stepError);

                    if (onStepError != null)
                        await onStepError(context, step.Name, ex).ConfigureAwait(false);

                    stopwatch?.Record(step.Name, isSuccess: false);
                    return PipelineResult.Failed(errors, stopwatch?.Timings);
                }
                catch (Exception ex)
                {
                    var stepError = new StepError(step.Name, ex, isFatal: false);
                    errors.Add(stepError);

                    if (onStepError != null)
                        await onStepError(context, step.Name, ex).ConfigureAwait(false);

                    stopwatch?.Record(step.Name, isSuccess: false);
                }
            }

            return errors.Count > 0
                ? PipelineResult.PartialSuccess(errors, stopwatch?.Timings)
                : PipelineResult.Success(stopwatch?.Timings);
        }
    }
}


