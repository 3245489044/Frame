
using EXContext = TConfig.ExecutionContext;

namespace TConfig;

public interface IPipelineExecutor
{
    Task<ExecutionResult> ExecutePipelineAsync(TrPipeline pipeline, EXContext? context = null);

    IPipelineBuilder CreateBuilder(string? name = null);
}
