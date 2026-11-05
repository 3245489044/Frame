
using Modules.Transformers;
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.Models;

namespace Share.LogicComposition.DefaultImplementations
{
    public class DefaultChainComposer<TElement> : IChainComposer<TElement>
    {
        private readonly ICompositionValidator? _validator;
        private readonly Func<TElement, string?>? _keyExtractor;
        private readonly IMergeStrategy<TElement>? _mergeStrategy;

        public DefaultChainComposer(
            ICompositionValidator? validator = null,
            Func<TElement, string?>? keyExtractor = null,
            IMergeStrategy<TElement>? mergeStrategy = null)
        {
            _validator = validator;
            _keyExtractor = keyExtractor;
            _mergeStrategy = mergeStrategy;
        }

        public MergeResult<IEnumerable<TElement>> Compose(
            IEnumerable<IEnumerable<TElement>> chains,
            CompositionOptions? options = null)
        {
            options ??= CompositionOptions.Default;
            var conflicts = new List<MergeConflict>();
            var stats = new MergeStatistics();

            var allElements = chains.SelectMany(c => c).ToList();
            stats.TotalItems = allElements.Count;

            if (_keyExtractor == null)
            {
                return MergeResult<IEnumerable<TElement>>.Success(
                    allElements, conflicts, stats);
            }

            switch (options.DuplicateHandling)
            {
                case DuplicateHandling.KeepBoth:
                    stats.AddedItems = allElements.Count;
                    return MergeResult<IEnumerable<TElement>>.Success(
                        allElements, conflicts, stats);

                case DuplicateHandling.Skip:
                    return ComposeWithGroupReducer(
                        allElements,
                        new KeepFirstReduce<TElement>(),
                        conflicts, stats,
                        isSkipMode: true);

                case DuplicateHandling.Overwrite:
                    return ComposeWithGroupReducer(
                        allElements,
                        new KeepLastReduce<TElement>(),
                        conflicts, stats);

                case DuplicateHandling.Merge:
                    if (_mergeStrategy != null)
                    {
                        return ComposeWithGroupReducer(
                            allElements,
                            new FuncReduce<TElement>((existing, newItem) =>
                            {
                                var mergeResult = _mergeStrategy.Merge(newItem, existing, null);
                                if (mergeResult.IsSuccess && mergeResult.Result != null)
                                    return mergeResult.Result;
                                return existing;
                            }),
                            conflicts, stats);
                    }
                    else
                    {
                        return ComposeWithGroupReducer(
                            allElements,
                            new KeepLastReduce<TElement>(),
                            conflicts, stats);
                    }

                default:
                    stats.AddedItems = allElements.Count;
                    return MergeResult<IEnumerable<TElement>>.Success(
                        allElements, conflicts, stats);
            }
        }

        private MergeResult<IEnumerable<TElement>> ComposeWithGroupReducer(
            List<TElement> allElements,
            IReduceStrategy<TElement> reduceStrategy,
            List<MergeConflict> conflicts,
            MergeStatistics stats,
            bool isSkipMode = false)
        {
            var matchStrategy = new KeySelectorMatch<TElement, string>(
                item => _keyExtractor!(item) ?? string.Empty);

            var reducer = new GroupReducer<TElement>(
                "ComposeDedupe",
                matchStrategy,
                reduceStrategy);

            var reduced = reducer.Transform(allElements).ToList();

            var seenKeys = new HashSet<string>();
            foreach (var element in allElements)
            {
                var key = _keyExtractor!(element) ?? string.Empty;
                if (!seenKeys.Add(key))
                {
                    var existing = reduced.FirstOrDefault(e =>
                        _keyExtractor!(e) == key);
                    if (existing != null)
                    {
                        conflicts.Add(new MergeConflict(
                            key,
                            element,
                            existing,
                            isSkipMode ? "Skipped" : "Overwritten"));
                    }
                }
            }

            stats.AddedItems = reduced.Count;
            stats.OverwrittenItems = conflicts.Count;
            stats.SkippedItems = allElements.Count - reduced.Count - conflicts.Count;

            return MergeResult<IEnumerable<TElement>>.Success(
                reduced, conflicts, stats);
        }

        public MergeResult<IEnumerable<TElement>> Insert(
            IEnumerable<TElement> chain,
            TElement element,
            InsertPosition position)
        {
            var list = chain.ToList();
            var conflicts = new List<MergeConflict>();

            switch (position.Mode)
            {
                case InsertMode.End:
                    list.Add(element);
                    break;
                case InsertMode.Start:
                    list.Insert(0, element);
                    break;
                case InsertMode.After:
                    if (position.TargetKey != null)
                    {
                        var idx = list.FindIndex(n =>
                            _keyExtractor?.Invoke(n) == position.TargetKey);
                        if (idx >= 0)
                            list.Insert(idx + 1, element);
                        else
                            list.Add(element);
                    }
                    break;
                case InsertMode.Before:
                    if (position.TargetKey != null)
                    {
                        var idx = list.FindIndex(n =>
                            _keyExtractor?.Invoke(n) == position.TargetKey);
                        if (idx >= 0)
                            list.Insert(idx, element);
                        else
                            list.Insert(0, element);
                    }
                    break;
                case InsertMode.AtIndex:
                    if (position.Index.HasValue &&
                        position.Index.Value >= 0 &&
                        position.Index.Value <= list.Count)
                        list.Insert(position.Index.Value, element);
                    else
                        list.Add(element);
                    break;
            }

            return MergeResult<IEnumerable<TElement>>.Success(list, conflicts);
        }

        public MergeResult<IEnumerable<TElement>> Connect(
            IEnumerable<TElement> sourceChain,
            IEnumerable<TElement> targetChain,
            ConnectionPoint sourcePoint,
            ConnectionPoint targetPoint)
        {
            var sourceList = sourceChain.ToList();
            var targetList = targetChain.ToList();
            var result = new List<TElement>(sourceList);

            int insertIndex = sourcePoint.Type switch
            {
                ConnectionPoint.ConnectionPointType.ChainEnd => sourceList.Count,
                ConnectionPoint.ConnectionPointType.ByElementKey
                    when sourcePoint.ElementKey != null =>
                    sourceList.FindIndex(n =>
                        _keyExtractor?.Invoke(n) == sourcePoint.ElementKey) + 1,
                _ => sourceList.Count
            };

            var elementsToInsert = targetPoint.Type switch
            {
                ConnectionPoint.ConnectionPointType.ChainStart => targetList,
                ConnectionPoint.ConnectionPointType.ChainEnd => targetList.Count > 0
                    ? new List<TElement> { targetList[^1] }
                    : new List<TElement>(),
                ConnectionPoint.ConnectionPointType.ByElementKey
                    when targetPoint.ElementKey != null =>
                    targetList.SkipWhile(n =>
                        _keyExtractor?.Invoke(n) != targetPoint.ElementKey).ToList(),
                _ => targetList
            };

            result.InsertRange(insertIndex, elementsToInsert);

            return MergeResult<IEnumerable<TElement>>.Success(result);
        }
    }
}
