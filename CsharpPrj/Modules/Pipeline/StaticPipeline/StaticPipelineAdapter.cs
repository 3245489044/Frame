
#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Modules.Pipeline.StaticPipeline
{

    public class StaticPipelineStageAdapter<TContext> : IPipelineStage<TContext>
        where TContext : IPipelineContext
    {
        private readonly StaticPipeline<TContext> _pipeline;

        public string Name { get; }

        public string Description { get; }

        public StaticPipelineStageAdapter(
            StaticPipeline<TContext> pipeline,
            string? name = null,
            string? description = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            Name = name ?? $"StaticPipeline ({pipeline.StepCount} steps)";
            Description = description ?? $"{Name} adapter";
        }

        public virtual bool CanExecute(TContext context) => true;

        public Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = _pipeline.Execute(context);

            if (result.IsSuccess)
            {
                context.SetStageResult(Name, true);
            }
            else if (result.HasFatalErrors)
            {
                var fatal = result.FirstFatalError!;
                context.SetStageResult(Name, false, fatal.ToString());
            }
            else
            {
                var errorMsg = string.Join("; ", result.Errors.Select(e => e.ToString()));
                context.SetStageResult(Name, false, errorMsg);
            }

            return Task.CompletedTask;
        }
    }


    public class StaticAsyncPipelineStageAdapter<TContext> : IPipelineStage<TContext>
        where TContext : IPipelineContext
    {
        private readonly StaticAsyncPipeline<TContext> _pipeline;

        public string Name { get; }

        public string Description { get; }

        public StaticAsyncPipelineStageAdapter(
            StaticAsyncPipeline<TContext> pipeline,
            string? name = null,
            string? description = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            Name = name ?? $"StaticAsyncPipeline ({pipeline.StepCount} steps)";
            Description = description ?? $"{Name} adapter";
        }

        public virtual bool CanExecute(TContext context) => true;

        public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _pipeline.ExecuteAsync(context).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                context.SetStageResult(Name, true);
            }
            else if (result.HasFatalErrors)
            {
                var fatal = result.FirstFatalError!;
                context.SetStageResult(Name, false, fatal.ToString());
            }
            else
            {
                var errorMsg = string.Join("; ", result.Errors.Select(e => e.ToString()));
                context.SetStageResult(Name, false, errorMsg);
            }
        }
    }
}


