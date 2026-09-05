
using Share.LogicComposition.Models;

namespace Share.LogicComposition.Abstractions
{
    public interface IMergeStrategy<T>
    {
        MergeResult<T> Merge(T source, T target, MergeOptions? options = null);
    }
}
