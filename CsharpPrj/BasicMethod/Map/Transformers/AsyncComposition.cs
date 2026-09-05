
namespace Modules.Transformers
{
    public static class AsyncTransformerComposition
    {
        public static IAsyncTransformer<TInput, TOutput> Then<TInput, TMiddle, TOutput>(
            this IAsyncTransformer<TInput, TMiddle> first,
            IAsyncTransformer<TMiddle, TOutput> second)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            return new ComposedAsyncTransformer<TInput, TMiddle, TOutput>(first, second);
        }
    }
}
