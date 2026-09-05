
using System;
using System.Threading;
using System.Threading.Tasks;
using TConfig.ContextFrame.Abstractions;

namespace TConfig
{
    public class FrameworkOptions
    {
        public bool EnableEventBus { get; set; } = true;

        public bool EnableEventBinding { get; set; } = true;

        public bool EnableCore { get; set; } = true;

        public bool EnableWinForms { get; set; } = false;

        public bool CliMode { get; set; } = false;

        public Action<string>? EventBusLogger { get; set; }

        public IParameterExecutionWorkflow? ParameterExecutionWorkflow { get; set; }


        public bool EnableCommandSystem { get; set; } = false;

        public bool EnablePipeline { get; set; } = false;

        public string? PipelineName { get; set; }

        public Action<object>? ConfigurePipeline { get; set; }

        public bool EnableDeduplicatingEventBus { get; set; } = false;

        public int DedupWindow { get; set; } = 300;

        public bool EnableEventQueue { get; set; } = false;

        public bool EnableEventScope { get; set; } = false;
    }

    public interface IParameterExecutionWorkflow
    {
        Task<ParameterExecutionResult> ExecuteAsync(
            ParameterExecutionRequest request,
            CancellationToken cancellationToken = default);
    }

    public static class FrameworkOptionsBuilderExtensions
    {
        public static IContext WithFrameworkOptions(
            this IContext context,
            FrameworkOptions options)
        {
            context.SetData(options.EnableEventBus, nameof(FrameworkOptions.EnableEventBus));
            context.SetData(options.EnableEventBinding, nameof(FrameworkOptions.EnableEventBinding));
            context.SetData(options.EnableCore, nameof(FrameworkOptions.EnableCore));
            context.SetData(options.EnableWinForms, nameof(FrameworkOptions.EnableWinForms));
            context.SetData(options.CliMode, nameof(FrameworkOptions.CliMode));
            return context;
        }
    }
}
