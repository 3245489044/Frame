
using Share.LogicComposition.Models;

namespace Share.LogicComposition.Abstractions
{
    public interface IChainComposer<TElement>
    {
        MergeResult<IEnumerable<TElement>> Compose(
            IEnumerable<IEnumerable<TElement>> chains,
            CompositionOptions? options = null);

        MergeResult<IEnumerable<TElement>> Insert(
            IEnumerable<TElement> chain,
            TElement element,
            InsertPosition position);

        MergeResult<IEnumerable<TElement>> Connect(
            IEnumerable<TElement> sourceChain,
            IEnumerable<TElement> targetChain,
            ConnectionPoint sourcePoint,
            ConnectionPoint targetPoint);
    }
}
