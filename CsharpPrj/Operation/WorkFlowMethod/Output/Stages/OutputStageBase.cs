
using Modules.Pipeline;

namespace WorkFlowMethod.Output.Stages;

public abstract class OutputStageBase : IPipelineStage
{
    public abstract string Name { get; }

    public abstract int Order { get; }

    public abstract bool IsFatal { get; }

    protected static OutputWriteContext? TryGetContext(object input, ErrorCollector errors, string stageName)
    {
        if (input is OutputWriteContext ctx)
            return ctx;

        errors.Add($"{stageName}_INVALID_INPUT",
            $"{stageName} 期望输入类型为 OutputWriteContext，实际为 {input?.GetType().Name}",
            ErrorLevel.Fatal);
        return null;
    }

    protected abstract object? ExecuteCore(OutputWriteContext ctx, ErrorCollector errors, PipelineContext context);

    public object? Process(object input, ErrorCollector errors, PipelineContext context)
    {
        var ctx = TryGetContext(input, errors, Name);
        if (ctx == null)
            return null;

        return ExecuteCore(ctx, errors, context);
    }
}
