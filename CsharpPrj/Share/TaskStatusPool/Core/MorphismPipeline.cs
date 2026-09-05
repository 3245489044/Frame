
#nullable enable
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Core
{
    public class MorphismPipeline<TContext> : IMorphismPipeline<TContext>
    {
        protected readonly List<IOrderedMorphism<TContext, TContext>> _items = new();
        private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);

        public string Name { get; }

        public string Description { get; }

        public int Count
        {
            get
            {
                _rwLock.EnterReadLock();
                try { return _items.Count; }
                finally { _rwLock.ExitReadLock(); }
            }
        }

        public MorphismPipeline(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public MorphismPipeline<TContext> Register(IOrderedMorphism<TContext, TContext> item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _rwLock.EnterWriteLock();
            try { _items.Add(item); }
            finally { _rwLock.ExitWriteLock(); }
            return this;
        }

        public MorphismPipeline<TContext> RegisterRange(IEnumerable<IOrderedMorphism<TContext, TContext>> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            _rwLock.EnterWriteLock();
            try { _items.AddRange(items); }
            finally { _rwLock.ExitWriteLock(); }
            return this;
        }

        public MorphismPipeline<TContext> InsertBefore(string name, IOrderedMorphism<TContext, TContext> item)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(item);

            _rwLock.EnterWriteLock();
            try
            {
                var index = FindIndexByName(name);
                if (index >= 0)
                    _items.Insert(index, item);
                else
                    _items.Add(item);
            }
            finally { _rwLock.ExitWriteLock(); }
            return this;
        }

        public MorphismPipeline<TContext> InsertAfter(string name, IOrderedMorphism<TContext, TContext> item)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(item);

            _rwLock.EnterWriteLock();
            try
            {
                var index = FindIndexByName(name);
                if (index >= 0)
                    _items.Insert(index + 1, item);
                else
                    _items.Add(item);
            }
            finally { _rwLock.ExitWriteLock(); }
            return this;
        }

        public MorphismPipeline<TContext> Remove(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            _rwLock.EnterWriteLock();
            try
            {
                var index = FindIndexByName(name);
                if (index >= 0)
                    _items.RemoveAt(index);
            }
            finally { _rwLock.ExitWriteLock(); }
            return this;
        }

        public void Clear()
        {
            _rwLock.EnterWriteLock();
            try { _items.Clear(); }
            finally { _rwLock.ExitWriteLock(); }
        }

        public TContext Execute(TContext context)
        {
            var sorted = GetSortedItems();

            foreach (var item in sorted)
            {
                context = item.Morph(context);
            }

            return context;
        }

        public async Task<TContext> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        {
            var sorted = GetSortedItems();

            foreach (var item in sorted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item is IAsyncMorphism<TContext, TContext> asyncItem)
                {
                    context = await asyncItem.MorphAsync(context, cancellationToken);
                }
                else
                {
                    context = item.Morph(context);
                }
            }

            return context;
        }

        private List<IOrderedMorphism<TContext, TContext>> GetSortedItems()
        {
            _rwLock.EnterReadLock();
            try
            {
                return _items.OrderBy(m => m.Order).ToList();
            }
            finally { _rwLock.ExitReadLock(); }
        }

        private int FindIndexByName(string name)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        public override string ToString()
            => $"MorphismPipeline[{Name}] Count={_items.Count}";
    }
}
