
#nullable enable

namespace Share.TaskStatusPool.Abstractions
{

    public interface IMorphism<in TIn, out TOut>
    {
        string Name { get; }

        TOut Morph(TIn input);
    }


    public interface IAsyncMorphism<in TIn, TOut>
    {
        string Name { get; }

        Task<TOut> MorphAsync(TIn input, CancellationToken cancellationToken = default);
    }


    public interface IOrderedMorphism<in TIn, out TOut> : IMorphism<TIn, TOut>
    {
        int Order { get; }
    }


    public interface IMorphismPipeline<TContext>
    {
        string Name { get; }

        TContext Execute(TContext context);

        Task<TContext> ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
    }
}
