
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Core;

public abstract class TransformBase<TInput, TOutput> : IJsonTransform<TInput, TOutput>
{
    public abstract string Name { get; }

    public abstract Task<TOutput> ProcessAsync(TInput input, JsonPipelineContext context);

    async Task<object?> IJsonTransform.ProcessAsyncUntyped(object? input, JsonPipelineContext context)
    {
        var result = await ProcessAsync((TInput)input!, context);
        return result;
    }
}
