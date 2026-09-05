
namespace TConfig;

public interface IPipelineBuilder
{
    IPipelineBuilder AddStep(string name, Delegate function);
    IPipelineBuilder AddStep(string name, IFunctionUnit unit);
    IPipelineBuilder AddStep<TInput, TOutput>(string name, Func<TInput, Task<TOutput>> asyncFunction);
    IPipelineBuilder AddConditionalStep(string name, Func<object?, bool> condition, Delegate function);
    IPipelineBuilder AddParallelSteps(IEnumerable<(string Name, Delegate Function)> steps);
    IPipelineBuilder AddCall(string name, params object?[] parameters);
    IPipelineBuilder AddSubPipeline(string name, Action<IPipelineBuilder> buildSub);
    IPipelineBuilder WithFlattenSingleResult(bool flatten = true);
    TrPipeline Build();
}
