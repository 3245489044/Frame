
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.SortMent
{

    public class SortMentCollection<TSortMent> : ISortMentCollection<TSortMent>
        where TSortMent : INamed
    {
        public IReadOnlyList<TSortMent> Items => _items.AsReadOnly();

        public int Count => _items.Count;

        protected readonly List<TSortMent> _items = new();

        public ISortMentCollection<TSortMent> Register(TSortMent item)
        {
            _items.Add(item);
            return this;
        }

        public ISortMentCollection<TSortMent> RegisterRange(IEnumerable<TSortMent> items)
        {
            _items.AddRange(items);
            return this;
        }

        public ISortMentCollection<TSortMent> InsertBefore(string name, TSortMent item)
        {
            var index = FindIndexByName(name);
            if (index >= 0)
                _items.Insert(index, item);
            else
                _items.Add(item);
            return this;
        }

        public ISortMentCollection<TSortMent> InsertAfter(string name, TSortMent item)
        {
            var index = FindIndexByName(name);
            if (index >= 0)
                _items.Insert(index + 1, item);
            else
                _items.Add(item);
            return this;
        }

        public ISortMentCollection<TSortMent> Remove(string name)
        {
            var index = FindIndexByName(name);
            if (index >= 0)
                _items.RemoveAt(index);
            return this;
        }

        public void Clear() => _items.Clear();

        private int FindIndexByName(string name)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }


    public class SortMentPipeline<TContext> : SortMentCollection<ISortableMent<TContext, TContext>>, ISortMentPipeline<TContext>
    {
        public string Name { get; }

        public string Description { get; }

        public SortMentPipeline(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public TContext Execute(TContext context)
        {
            var sorted = _items.OrderBy(s => s.Order).ToList();

            foreach (var item in sorted)
            {
                context = item.Morph(context);
            }

            return context;
        }

        public async Task<TContext> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        {
            var sorted = _items.OrderBy(s => s.Order).ToList();

            foreach (var item in sorted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item is IAsyncSortMent<TContext, TContext> asyncItem)
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
    }
}
