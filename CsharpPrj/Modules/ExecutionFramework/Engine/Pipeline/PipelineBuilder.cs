using TConfig;
using EXContext = TConfig.ExecutionContext;

namespace ExecutionFramework.Engine;

public class PipelineBuilder : TConfig.IPipelineBuilder
{
    private readonly ITrConfigRegistry _registry;
    private readonly TrPipeline _pipeline;

    internal string? Name
    {
        get => _pipeline.Name;
        set => _pipeline.Name = value ?? "Pipeline";
    }

    public PipelineBuilder(ITrConfigRegistry? registry = null)
    {
        _registry = registry ?? new TrConfigRegistry();
        _pipeline = new TrPipeline();
    }

    public TConfig.IPipelineBuilder AddStep(string name, Delegate function)

    {
        _pipeline.Steps.Add(new TrStep
        {
            Name = name,
            Function = function ?? throw new ArgumentNullException(nameof(function))
        });
        return this;
    }

    public TConfig.IPipelineBuilder AddStep(string name, TConfig.IFunctionUnit unit)


    {
        if (unit == null) throw new ArgumentNullException(nameof(unit));

        Func<object?, Task<object?>> wrappedFunction = async (input) =>
        {
            var context = input as EXContext ?? new EXContext();
            var result = await unit.ExecuteAsync(input, context);
            return result.Value;
        };

        _pipeline.Steps.Add(new TrStep
        {
            Name = name,
            Function = wrappedFunction
        });
        return this;
    }

    public TConfig.IPipelineBuilder AddStep<TInput, TOutput>(string name, Func<TInput, Task<TOutput>> asyncFunction)

    {
        _pipeline.Steps.Add(new TrStep
        {
            Name = name,
            Function = asyncFunction as Delegate ?? throw new ArgumentNullException(nameof(asyncFunction))
        });
        return this;
    }

    public TConfig.IPipelineBuilder AddConditionalStep(string name, Func<object?, bool> condition, Delegate function)

    {
        _pipeline.Steps.Add(new TrStep
        {
            Name = name,
            Condition = condition ?? throw new ArgumentNullException(nameof(condition)),
            Function = function ?? throw new ArgumentNullException(nameof(function))
        });
        return this;
    }

    public TConfig.IPipelineBuilder AddParallelSteps(IEnumerable<(string Name, Delegate Function)> steps)

    {
        var parallelSteps = (steps ?? throw new ArgumentNullException(nameof(steps)))
            .Select(s => new TrStep { Name = s.Name, Function = s.Function })
            .ToList();

        _pipeline.Steps.Add(new TrStep
        {
            Name = "Parallel",
            IsParallel = true,
            ParallelSteps = parallelSteps
        });
        return this;
    }

    public TConfig.IPipelineBuilder AddCall(string name, params object?[] parameters)

    {
        _pipeline.Steps.Add(new TrStep
        {
            Name = name,
            UseExplicitParameters = true,
            Parameters = parameters?.ToList() ?? new List<object?>()
        });
        return this;
    }

    public TConfig.IPipelineBuilder AddSubPipeline(string name, Action<TConfig.IPipelineBuilder> buildSub)

    {
        var subBuilder = new PipelineBuilder(_registry) { Name = name };
        buildSub(subBuilder);
        _pipeline.Steps.Add(new TrStep
        {
            Name = name,
            SubPipeline = subBuilder.Build()
        });
        return this;
    }

    public TConfig.IPipelineBuilder WithFlattenSingleResult(bool flatten = true)

    {
        _pipeline.FlattenSingleResult = flatten;
        return this;
    }

    public TrPipeline Build()
    {
        return _pipeline;
    }
}
