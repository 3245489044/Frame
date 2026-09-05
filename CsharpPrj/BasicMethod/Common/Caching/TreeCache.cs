#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Modules.Common
{
    public class TreeCache<TNode, TData>
        where TNode : class
        where TData : class, new()
    {
        private readonly WeakStateManager<TNode, TData> _cache = new();
        private readonly Func<TNode, IEnumerable<TNode>> _childrenSelector;

        public TreeCache(Func<TNode, IEnumerable<TNode>> childrenSelector)
        {
            _childrenSelector = childrenSelector ?? throw new ArgumentNullException(nameof(childrenSelector));
        }

        public void CacheRecursive(TNode root, Func<TNode, TData> dataFactory)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (dataFactory == null) throw new ArgumentNullException(nameof(dataFactory));

            var data = dataFactory(root);
            var existing = _cache.GetOrCreate(root);
            CopyProperties(data, existing);

            foreach (var child in _childrenSelector(root))
            {
                if (child != null)
                {
                    CacheRecursive(child, dataFactory);
                }
            }
        }

        public bool TryGet(TNode node, [MaybeNullWhen(false)] out TData data)
            => _cache.TryGet(node, out data);

        public void Remove(TNode node) => _cache.Remove(node);

        public void Clear() => _cache.Clear();

        private static void CopyProperties(TData source, TData target)
        {
            if (source == null || target == null) return;
            var properties = typeof(TData).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (prop.CanRead && prop.CanWrite)
                {
                    var value = prop.GetValue(source);
                    prop.SetValue(target, value);
                }
            }
        }
    }
}
