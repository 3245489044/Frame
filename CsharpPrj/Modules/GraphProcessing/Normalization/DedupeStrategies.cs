
#nullable enable

using Modules.Transformers;
using GraphFoundation.Topology.Transformers;

namespace Modules.GraphProcessing.Normalization
{

    public class SourceTargetMatch : IMatchStrategy<Connection, (string, string)>
    {
        public (string, string) GetMatchKey(Connection item)
            => (item.Source, item.Target);
    }

    public class UndirectedMatch : IMatchStrategy<Connection, (string, string)>
    {
        public (string, string) GetMatchKey(Connection item)
        {
            var src = item.Source;
            var tgt = item.Target;
            if (string.Compare(src, tgt, StringComparison.Ordinal) > 0)
                return (tgt, src);
            return (src, tgt);
        }
    }

    public class SourceTargetDirectionMatch : IMatchStrategy<Connection, (string, string, bool)>
    {
        public (string, string, bool) GetMatchKey(Connection item)
            => (item.Source, item.Target, item.IsDirected);
    }

    public class FullMatch : IMatchStrategy<Connection, (string, string, bool, double)>
    {
        public (string, string, bool, double) GetMatchKey(Connection item)
            => (item.Source, item.Target, item.IsDirected, item.Weight);
    }



    public class KeepFirstReduce : IReduceStrategy<Connection>
    {
        public Connection Reduce(Connection existing, Connection newItem)
            => existing;
    }

    public class KeepLastReduce : IReduceStrategy<Connection>
    {
        public Connection Reduce(Connection existing, Connection newItem)
            => newItem;
    }

    public class KeepHigherWeightReduce : IReduceStrategy<Connection>
    {
        public Connection Reduce(Connection existing, Connection newItem)
            => newItem.Weight > existing.Weight ? newItem : existing;
    }

    public class KeepLowerWeightReduce : IReduceStrategy<Connection>
    {
        public Connection Reduce(Connection existing, Connection newItem)
            => newItem.Weight < existing.Weight ? newItem : existing;
    }

    public class KeepMinIdReduce : IReduceStrategy<Connection>
    {
        public Connection Reduce(Connection existing, Connection newItem)
            => string.Compare(newItem.Id, existing.Id, StringComparison.Ordinal) < 0
                ? newItem : existing;
    }


    internal class ObjectMatchAdapter<T, TKey> : IMatchStrategy<T, object>
        where TKey : notnull
    {
        private readonly IMatchStrategy<T, TKey> _inner;

        public ObjectMatchAdapter(IMatchStrategy<T, TKey> inner)
        {
            _inner = inner;
        }

        public object GetMatchKey(T item) => _inner.GetMatchKey(item);
    }

    internal static class MatchStrategyExtensions
    {
        public static IMatchStrategy<T, object> AsObjectMatch<T, TKey>(this IMatchStrategy<T, TKey> strategy)
            where TKey : notnull
            => new ObjectMatchAdapter<T, TKey>(strategy);
    }
}
