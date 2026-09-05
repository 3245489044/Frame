
using TConfig;
using EXContext = TConfig.ExecutionContext;

namespace ExecutionFramework.Abstractions;

public interface IParameterProcessor
{
    Task<object> ProcessAsync(object input, ITrConfigRegistry registry, EXContext? context = null);

    Task<IEnumerable<object>> ProcessManyAsync(IEnumerable<object> inputs, ITrConfigRegistry registry, EXContext? context = null);
}
