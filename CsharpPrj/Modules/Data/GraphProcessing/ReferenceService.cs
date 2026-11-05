
#nullable enable

using GraphFoundation.Abstractions;
using Modules.GraphProcessing.Utilities;

namespace Modules.GraphProcessing.Reference
{

    public enum CrossChainReferenceStrategy
    {
        Preserve,
        Rewrite,
        Ignore
    }

    public interface IReferenceResolver<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        object Resolve(object value, IReadOnlyGraph<TKey, TNodeData, object> graph);
    }


    public class DefaultReferenceResolver<TKey, TNodeData> : IReferenceResolver<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly Func<object, IReadOnlyGraph<TKey, TNodeData, object>, object> _resolveFunc;

        public DefaultReferenceResolver(Func<object, IReadOnlyGraph<TKey, TNodeData, object>, object>? resolveFunc = null)
        {
            _resolveFunc = resolveFunc ?? DefaultResolve;
        }

        public object Resolve(object value, IReadOnlyGraph<TKey, TNodeData, object> graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            return _resolveFunc(value, graph);
        }

        private object DefaultResolve(object value, IReadOnlyGraph<TKey, TNodeData, object> graph)
        {
            if (value == null) return null!;

            if (value is string str)
            {
                try
                {
                    var key = (TKey)Convert.ChangeType(str, typeof(TKey));
                    return graph.ContainsNode(key) ? key : str;
                }
                catch
                {
                    return str;
                }
            }

            if (value is System.Collections.IDictionary dict)
            {
                var newDict = new Dictionary<object, object>();
                foreach (var key in dict.Keys)
                    newDict[key] = Resolve(dict[key], graph);
                return newDict;
            }

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                    list.Add(Resolve(item, graph));
                return list;
            }

            return value;
        }
    }

    public class ReferenceRewriter<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReadOnlyDictionary<TKey, TKey> _idMap;
        private readonly CrossChainReferenceStrategy _strategy;

        public ReferenceRewriter(IReadOnlyDictionary<TKey, TKey> idMap, CrossChainReferenceStrategy strategy = CrossChainReferenceStrategy.Rewrite)
        {
            _idMap = idMap ?? throw new ArgumentNullException(nameof(idMap));
            _strategy = strategy;
        }

        public TNodeData Rewrite(TNodeData data, IReadOnlyGraph<TKey, TNodeData, object> graph)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            if (_strategy == CrossChainReferenceStrategy.Ignore)
                return data;

            if (_strategy == CrossChainReferenceStrategy.Preserve)
                return data;

            var cloned = DeepCopyUtility.DeepCopy(data);
            if (cloned == null)
                throw new InvalidOperationException("Deep copy returned null.");

            var result = ObjectTraversalUtility.Traverse(cloned, obj =>
            {
                if (obj is string str)
                {
                    var key = ConvertKey(str);
                    if (key != null && _idMap.TryGetValue(key, out var newKey))
                        return newKey.ToString()!;
                }
                return obj;
            });

            return result;
        }

        private TKey? ConvertKey(string str)
        {
            try
            {
                return (TKey)Convert.ChangeType(str, typeof(TKey));
            }
            catch
            {
                return default;
            }
        }
    }

    public class DeepReferenceUpdater<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReferenceResolver<TKey, TNodeData> _resolver;

        public DeepReferenceUpdater(IReferenceResolver<TKey, TNodeData> resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public T Update<T>(T value, IReadOnlyGraph<TKey, TNodeData, object> graph)
        {
            return DeepReferenceUpdaterUtility.UpdateReferences(value, _resolver, graph);
        }
    }

    public static class DeepReferenceUpdaterUtility
    {
        public static T UpdateReferences<T, TKey, TNodeData>(
            T obj,
            IReferenceResolver<TKey, TNodeData> resolver,
            IReadOnlyGraph<TKey, TNodeData, object> graph)
            where TKey : notnull
            where TNodeData : class
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            return ObjectTraversalUtility.Traverse(obj, item => resolver.Resolve(item, graph));
        }
    }


    public class ReferenceService<TKey, TNodeData>
        where TKey : notnull
        where TNodeData : class
    {
        public ReferenceRewriter<TKey, TNodeData> CreateRewriter(
            IReadOnlyDictionary<TKey, TKey> idMap,
            CrossChainReferenceStrategy strategy = CrossChainReferenceStrategy.Rewrite)
        {
            return new ReferenceRewriter<TKey, TNodeData>(idMap, strategy);
        }

        public DefaultReferenceResolver<TKey, TNodeData> CreateResolver(
            Func<object, IReadOnlyGraph<TKey, TNodeData, object>, object>? resolveFunc = null)
        {
            return new DefaultReferenceResolver<TKey, TNodeData>(resolveFunc);
        }

        public DeepReferenceUpdater<TKey, TNodeData> CreateUpdater(
            IReferenceResolver<TKey, TNodeData> resolver)
        {
            return new DeepReferenceUpdater<TKey, TNodeData>(resolver);
        }

        public TNodeData RewriteReferences(
            TNodeData data,
            IReadOnlyDictionary<TKey, TKey> idMap,
            IReadOnlyGraph<TKey, TNodeData, object> graph,
            CrossChainReferenceStrategy strategy = CrossChainReferenceStrategy.Rewrite)
        {
            var rewriter = new ReferenceRewriter<TKey, TNodeData>(idMap, strategy);
            return rewriter.Rewrite(data, graph);
        }

        public T UpdateReferences<T>(
            T obj,
            IReferenceResolver<TKey, TNodeData> resolver,
            IReadOnlyGraph<TKey, TNodeData, object> graph)
        {
            return DeepReferenceUpdaterUtility.UpdateReferences(obj, resolver, graph);
        }
    }
}
