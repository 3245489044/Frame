using TConfig;
using EXContext = TConfig.ExecutionContext;

namespace ExecutionFramework.Abstractions;

public class NullParameterProcessor : IParameterProcessor
{
    public Task<object> ProcessAsync(object input, ITrConfigRegistry registry, EXContext? context = null)
        => Task.FromResult(input);

    public Task<IEnumerable<object>> ProcessManyAsync(IEnumerable<object> inputs, ITrConfigRegistry registry, EXContext? context = null)
        => Task.FromResult(inputs);
}
