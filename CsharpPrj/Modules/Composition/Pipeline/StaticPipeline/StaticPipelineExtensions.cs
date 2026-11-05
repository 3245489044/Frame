
#nullable enable
using System;
using System.Collections.Generic;

namespace Modules.Pipeline.StaticPipeline
{

    public class StaticPipelineBuilder<TContext>
    {
        private readonly List<Step<TContext>> _steps = new();
        private Action<TContext>? _onBeforeStep;
        private Action<TContext, string>? _onAfterStep;
        private Action<TContext, string, Exception>? _onStepError;
        private bool _enableTiming;

        internal StaticPipelineBuilder() { }

        public StaticPipelineBuilder<TContext> AddStep(
            string name,
            Action<TContext> execute,
            bool isFatal = true,
            StepCanExecute<TContext>? canExecute = null)
        {
            _steps.Add(new Step<TContext>(name, execute, isFatal, canExecute));
            return this;
        }

        public StaticPipelineBuilder<TContext> OnBeforeStep(Action<TContext> callback)
        {
            _onBeforeStep = callback;
            return this;
        }

        public StaticPipelineBuilder<TContext> OnAfterStep(Action<TContext, string> callback)
        {
            _onAfterStep = callback;
            return this;
        }

        public StaticPipelineBuilder<TContext> OnStepError(Action<TContext, string, Exception> callback)
        {
            _onStepError = callback;
            return this;
        }

        public StaticPipelineBuilder<TContext> EnableTiming()
        {
            _enableTiming = true;
            return this;
        }

        public StaticPipeline<TContext> Build()
            => new(_steps, _onBeforeStep, _onAfterStep, _onStepError, _enableTiming);
    }


    public class StaticAsyncPipelineBuilder<TContext>
    {
        private readonly List<AsyncStep<TContext>> _steps = new();
        private Func<TContext, System.Threading.Tasks.Task>? _onBeforeStep;
        private Func<TContext, string, System.Threading.Tasks.Task>? _onAfterStep;
        private Func<TContext, string, Exception, System.Threading.Tasks.Task>? _onStepError;
        private bool _enableTiming;

        internal StaticAsyncPipelineBuilder() { }

        public StaticAsyncPipelineBuilder<TContext> AddStep(
            string name,
            Func<TContext, System.Threading.Tasks.Task> execute,
            bool isFatal = true,
            StepCanExecute<TContext>? canExecute = null)
        {
            _steps.Add(new AsyncStep<TContext>(name, execute, isFatal, canExecute));
            return this;
        }

        public StaticAsyncPipelineBuilder<TContext> OnBeforeStep(Func<TContext, System.Threading.Tasks.Task> callback)
        {
            _onBeforeStep = callback;
            return this;
        }

        public StaticAsyncPipelineBuilder<TContext> OnAfterStep(Func<TContext, string, System.Threading.Tasks.Task> callback)
        {
            _onAfterStep = callback;
            return this;
        }

        public StaticAsyncPipelineBuilder<TContext> OnStepError(Func<TContext, string, Exception, System.Threading.Tasks.Task> callback)
        {
            _onStepError = callback;
            return this;
        }

        public StaticAsyncPipelineBuilder<TContext> EnableTiming()
        {
            _enableTiming = true;
            return this;
        }

        public StaticAsyncPipeline<TContext> Build()
            => new(_steps, _onBeforeStep, _onAfterStep, _onStepError, _enableTiming);
    }


    public static class Pipeline
    {
        public static StaticPipelineBuilder<TContext> Create<TContext>()
            => new();

        public static StaticAsyncPipelineBuilder<TContext> CreateAsync<TContext>()
            => new();
    }


    public static class StaticPipelineExtensions
    {
        public static StaticPipeline<TContext> Build<TContext>(
            this IEnumerable<Step<TContext>> steps)
            => new(steps);

        public static StaticAsyncPipeline<TContext> BuildAsync<TContext>(
            this IEnumerable<AsyncStep<TContext>> steps)
            => new(steps);
    }

}
