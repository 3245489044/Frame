
using CommandFramework.Abstractions;
using CommandFramework.Models;
using FrameworkAbstractions.Controls;
using Microsoft.Extensions.Logging;
using BP = Modules.Pipeline;


namespace CommandFramework.Pipeline
{
    public abstract class PipelineStageBase<TContext> : BP.IPipelineStage<TContext>
        where TContext : BP.IPipelineContext
    {
        public string Name { get; }

        public string Description { get; }

        protected PipelineStageBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? $"{name} stage";
        }

        public virtual bool CanExecute(TContext context)
        {
            return true;
        }

        public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteCoreAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ((BP.IPipelineContext)context).SetStageResult(Name, false, $"{Name} was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                ((BP.IPipelineContext)context).SetStageResult(Name, false, $"{Name} error: {ex.Message}");
            }
        }

        protected abstract Task ExecuteCoreAsync(TContext context, CancellationToken cancellationToken);
    }

    public abstract class CommandStageBase : PipelineStageBase<CommandContext>
    {
        protected bool EventCommandOnly { get; }

        protected CommandStageBase(string name, string? description = null, bool eventCommandOnly = false)
            : base(name, description)
        {
            EventCommandOnly = eventCommandOnly;
        }

        public override bool CanExecute(CommandContext context)
        {
            if (!EventCommandOnly)
                return true;
            return IsEventCommand(context);
        }

        protected static bool IsEventCommand(CommandContext context)
        {
            if (context.ParsedCommand?.IsNonEventCommand == true)
                return false;
            if (context.ParsedCommand?.NormalizedCommand == null)
                return false;
            return true;
        }

        protected static T? ResolveDependency<T>(
            CommandContext context,
            T? injected,
            string? sharedDataKey = null) where T : class
        {
            if (injected != null)
                return injected;

            if (sharedDataKey != null &&
                context.SharedData.TryGetValue(sharedDataKey, out var obj) &&
                obj is T typed)
            {
                return typed;
            }

            if (context.SharedData.TryGetValue("__ServiceProvider", out var sp) &&
                sp is IServiceProvider serviceProvider)
            {
                var service = serviceProvider.GetService(typeof(T));
                if (service is T typedService)
                    return typedService;
            }

            return null;
        }

}
}
