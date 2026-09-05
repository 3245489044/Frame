using BasicExecutionResult = TConfig.ExecutionResult;
using ExecutionFramework.Abstractions;

namespace WorkflowExecution.Pipeline;

using Abstractions;

public static class PipelineStages
{
    public static IPipelineStage CreateConditionalStage(
        Func<TConfig.IExecutionContext, bool> condition,
        IPipelineStage trueStage,
        IPipelineStage? falseStage = null,
        int order = 2)
    {
        return new StageRunner(
            "Conditional",
            order,
            async (context, cancellationToken) =>
            {
                if (condition(context))
                {
                    return await trueStage.ExecuteAsync(context, cancellationToken);
                }

                if (falseStage != null)
                {
                    return await falseStage.ExecuteAsync(context, cancellationToken);
                }

                return BasicExecutionResult.Success(null);
            });
    }

    public static IPipelineStage CreateParallelStage(
        IEnumerable<IPipelineStage> stages,
        int order = 2)
    {
        return new StageRunner(
            "Parallel",
            order,
            async (context, cancellationToken) =>
            {
                try
                {
                    var tasks = stages.Select(s => s.ExecuteAsync(context, cancellationToken));
                    var results = await Task.WhenAll(tasks);

                    var failedResult = results.FirstOrDefault(r => !r.IsSuccess);
                    if (failedResult != null)
                    {
                        return BasicExecutionResult.Failure(
                            $"Parallel stage failed: {failedResult.ErrorMessage}",
                            failedResult.Exception);
                    }

                    return BasicExecutionResult.Success(results.Select(r => r.Value).ToList());
                }
                catch (AggregateException ae)
                {
                    var firstError = ae.InnerExceptions.FirstOrDefault();
                    var messages = string.Join("; ", ae.InnerExceptions.Select(e => e.Message));
                    return BasicExecutionResult.Failure(
                        $"Parallel stage failed with {ae.InnerExceptions.Count} error(s): {messages}",
                        firstError);
                }
            });
    }

    public static IPipelineStage CreateLoggingStage(
        string message,
        int order = 0)
    {
        return new StageRunner(
            "Logging",
            order,
            (context, cancellationToken) =>
            {
                WorkFlowMethodLog.Log("PipelineStages_LoggingStage", new { message });
                return Task.FromResult(BasicExecutionResult.Success(null));
            });
    }

}


public class StageRunner : IPipelineStage
{
    public string Name { get; }
    public float Order { get; }
    private readonly Func<TConfig.IExecutionContext, CancellationToken, Task<BasicExecutionResult>> _executeFunc;

    public StageRunner(
        string name,
        float order,
        Func<TConfig.IExecutionContext, CancellationToken, Task<BasicExecutionResult>> executeFunc)
    {
        Name = name;
        Order = order;
        _executeFunc = executeFunc;
    }

    public Task<BasicExecutionResult> ExecuteAsync(TConfig.IExecutionContext context, CancellationToken cancellationToken)
        => _executeFunc(context, cancellationToken);
}
