
using Modules.Transformers;

namespace Share.LogicComposition.DefaultImplementations
{
    public class ObjectMergeTransformer<T> : ITransformer<(T source, T target), T>
        where T : class
    {
        public string Name => $"ObjectMerge<{typeof(T).Name}>";

        public T Transform((T source, T target) input)
        {
            ObjectMerger.Merge(input.target, input.source);
            return input.target;
        }
    }
}
