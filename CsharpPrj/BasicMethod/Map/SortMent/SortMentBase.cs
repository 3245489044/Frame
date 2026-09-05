
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.SortMent
{

    public abstract class SortMentBase<TSource, TTarget> : ISortMent<TSource, TTarget>
    {
        public string Name { get; }

        public string Description { get; }

        protected SortMentBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public abstract TTarget Morph(TSource source);
    }


    public abstract class AsyncSortMentBase<TSource, TTarget> : IAsyncSortMent<TSource, TTarget>
    {
        public string Name { get; }

        public string Description { get; }

        protected AsyncSortMentBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public abstract Task<TTarget> MorphAsync(TSource source, CancellationToken cancellationToken = default);
    }


    public abstract class SortableMentBase<TSource, TTarget> : SortMentBase<TSource, TTarget>, ISortableMent<TSource, TTarget>
    {
        public int Order { get; }

        protected SortableMentBase(string name, int order = 0, string? description = null)
            : base(name, description)
        {
            Order = order;
        }
    }


    public abstract class OrderedMentBase<TSource, TTarget> : SortableMentBase<TSource, TTarget>, IOrderedMent<TSource, TTarget>
    {
        public int SubOrder { get; }

        protected OrderedMentBase(string name, int order = 0, int subOrder = 0, string? description = null)
            : base(name, order, description)
        {
            SubOrder = subOrder;
        }
    }


    public class DelegateSortMent<TSource, TTarget> : SortMentBase<TSource, TTarget>
    {
        private readonly Func<TSource, TTarget> _morphFunc;

        public DelegateSortMent(string name, Func<TSource, TTarget> morphFunc, string? description = null)
            : base(name, description)
        {
            _morphFunc = morphFunc ?? throw new ArgumentNullException(nameof(morphFunc));
        }

        public override TTarget Morph(TSource source) => _morphFunc(source);
    }


    public class DelegateSortableMent<TSource, TTarget> : DelegateSortMent<TSource, TTarget>, ISortableMent<TSource, TTarget>
    {
        public int Order { get; }

        public DelegateSortableMent(string name, Func<TSource, TTarget> morphFunc, int order = 0, string? description = null)
            : base(name, morphFunc, description)
        {
            Order = order;
        }
    }


    public class DelegateAsyncSortMent<TSource, TTarget> : AsyncSortMentBase<TSource, TTarget>
    {
        private readonly Func<TSource, CancellationToken, Task<TTarget>> _morphFunc;

        public DelegateAsyncSortMent(string name, Func<TSource, CancellationToken, Task<TTarget>> morphFunc, string? description = null)
            : base(name, description)
        {
            _morphFunc = morphFunc ?? throw new ArgumentNullException(nameof(morphFunc));
        }

        public override Task<TTarget> MorphAsync(TSource source, CancellationToken cancellationToken = default)
            => _morphFunc(source, cancellationToken);
    }
}
