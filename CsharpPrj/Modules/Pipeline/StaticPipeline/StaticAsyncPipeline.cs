
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Modules.Pipeline.StaticPipeline
{

    public readonly record struct AsyncStep<TContext>(
        string Name,
        Func<TContext, Task> Execute,
        bool IsFatal = true,
        StepCanExecute<TContext>? CanExecute = null);


    public class StaticAsyncPipeline<TContext>
    {
        private readonly IReadOnlyList<AsyncStep<TContext>> _steps;
        private readonly Func<TContext, Task>? _onBeforeStep;
        private readonly Func<TContext, string, Task>? _onAfterStep;
        private readonly Func<TContext, string, Exception, Task>? _onStepError;
        private readonly bool _enableTiming;

        public StaticAsyncPipeline(
            IEnumerable<AsyncStep<TContext>> steps,
            Func<TContext, Task>? onBeforeStep = null,
            Func<TContext, string, Task>? onAfterStep = null,
            Func<TContext, string, Exception, Task>? onStepError = null,
            bool enableTiming = false)
        {
            _steps = new List<AsyncStep<TContext>>(steps ?? throw new ArgumentNullException(nameof(steps)))
                .AsReadOnly();
            _onBeforeStep = onBeforeStep;
            _onAfterStep = onAfterStep;
            _onStepError = onStepError;
            _enableTiming = enableTiming;

            if (_steps.Count == 0)
                throw new ArgumentException("至少需要一个步骤", nameof(steps));
        }

        public int StepCount => _steps.Count;

        public IReadOnlyList<AsyncStep<TContext>> Steps => _steps;

        public async Task<PipelineResult> ExecuteAsync(TContext context)
        {
            var stopwatch = _enableTiming ? new PipelineStopwatch() : null;

            return await PipelineExecutor.ExecuteAsync(
                _steps,
                context,
                _onBeforeStep,
                _onAfterStep,
                _onStepError,
                stopwatch).ConfigureAwait(false);
        }

        public async Task ExecuteOrThrowAsync(TContext context)
        {
            var result = await ExecuteAsync(context).ConfigureAwait(false);

            if (result.IsSuccess)
                return;

            if (result.HasFatalErrors)
            {
                var fatal = result.FirstFatalError!;
                throw new StepExecutionException(fatal.StepName, fatal.Exception);
            }
        }
    }
}
