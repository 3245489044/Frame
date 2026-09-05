
#nullable enable
using System;
using System.Collections.Generic;

namespace Modules.Pipeline.StaticPipeline
{

    public readonly record struct Step<TContext>(
        string Name,
        Action<TContext> Execute,
        bool IsFatal = true,
        StepCanExecute<TContext>? CanExecute = null);


    public class StaticPipeline<TContext>
    {
        private readonly IReadOnlyList<Step<TContext>> _steps;
        private readonly Action<TContext>? _onBeforeStep;
        private readonly Action<TContext, string>? _onAfterStep;
        private readonly Action<TContext, string, Exception>? _onStepError;
        private readonly bool _enableTiming;

        public StaticPipeline(
            IEnumerable<Step<TContext>> steps,
            Action<TContext>? onBeforeStep = null,
            Action<TContext, string>? onAfterStep = null,
            Action<TContext, string, Exception>? onStepError = null,
            bool enableTiming = false)
        {
            _steps = new List<Step<TContext>>(steps ?? throw new ArgumentNullException(nameof(steps)))
                .AsReadOnly();
            _onBeforeStep = onBeforeStep;
            _onAfterStep = onAfterStep;
            _onStepError = onStepError;
            _enableTiming = enableTiming;

            if (_steps.Count == 0)
                throw new ArgumentException("至少需要一个步骤", nameof(steps));
        }

        public int StepCount => _steps.Count;

        public IReadOnlyList<Step<TContext>> Steps => _steps;

        public PipelineResult Execute(TContext context)
        {
            var stopwatch = _enableTiming ? new PipelineStopwatch() : null;

            return PipelineExecutor.Execute(
                _steps,
                context,
                _onBeforeStep,
                _onAfterStep,
                _onStepError,
                stopwatch);
        }

        public void ExecuteOrThrow(TContext context)
        {
            var result = Execute(context);

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
