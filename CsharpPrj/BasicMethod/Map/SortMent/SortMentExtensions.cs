
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.SortMent
{
    public static class SortMentExtensions
    {

        public static ISortMent<TSource, TTarget> ToSortMent<TSource, TTarget>(
            this Func<TSource, TTarget> func,
            string name,
            string? description = null)
        {
            return new DelegateSortMent<TSource, TTarget>(name, func, description);
        }

        public static ISortableMent<TSource, TTarget> ToSortableMent<TSource, TTarget>(
            this Func<TSource, TTarget> func,
            string name,
            int order = 0,
            string? description = null)
        {
            return new DelegateSortableMent<TSource, TTarget>(name, func, order, description);
        }


        public static ISortableMent<TSource, TTarget> WithOrder<TSource, TTarget>(
            this ISortMent<TSource, TTarget> sortMent,
            int order)
        {
            return new SortableWrapper<TSource, TTarget>(sortMent, order);
        }


        public static ISortMent<TSource, TTarget> Then<TSource, TMiddle, TTarget>(
            this ISortMent<TSource, TMiddle> first,
            ISortMent<TMiddle, TTarget> next)
        {
            return new ComposedSortMent<TSource, TMiddle, TTarget>(first, next);
        }

        public static ISortMent<TSource, TTarget> Then<TSource, TMiddle, TTarget>(
            this ISortMent<TSource, TMiddle> first,
            Func<TMiddle, TTarget> nextFunc,
            string nextName)
        {
            var next = nextFunc.ToSortMent<TMiddle, TTarget>(nextName);
            return new ComposedSortMent<TSource, TMiddle, TTarget>(first, next);
        }


        public static IEnumerable<TTarget> GroupMorph<TSource, TKey, TTarget>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<IGrouping<TKey, TSource>, TTarget> groupMent)
            where TKey : notnull
        {
            return source
                .GroupBy(keySelector)
                .Select(groupMent);
        }

        public static IEnumerable<TTarget> GroupMorph<TSource, TKey, TTarget>(
            this IEnumerable<TSource> source,
            IGroupMent<TSource, TKey, TTarget> groupMent)
            where TKey : notnull
        {
            return groupMent.Morph(source);
        }


        public static SortMentPipeline<TContext> CreatePipeline<TContext>(
            string name,
            string? description = null)
        {
            return new SortMentPipeline<TContext>(name, description);
        }

        public static SortMentPipeline<TContext> Compose<TContext>(
            string name,
            params ISortableMent<TContext, TContext>[] items)
        {
            var pipeline = new SortMentPipeline<TContext>(name);
            pipeline.RegisterRange(items);
            return pipeline;
        }
    }


    internal class SortableWrapper<TSource, TTarget> : ISortableMent<TSource, TTarget>
    {
        private readonly ISortMent<TSource, TTarget> _inner;

        public string Name => _inner.Name;
        public string Description => _inner.Description;
        public int Order { get; }

        public SortableWrapper(ISortMent<TSource, TTarget> inner, int order)
        {
            _inner = inner;
            Order = order;
        }

        public TTarget Morph(TSource source) => _inner.Morph(source);
    }


    internal class ComposedSortMent<TSource, TMiddle, TTarget> : ISortMent<TSource, TTarget>
    {
        private readonly ISortMent<TSource, TMiddle> _first;
        private readonly ISortMent<TMiddle, TTarget> _next;

        public string Name => $"{_next.Name} ∘ {_first.Name}";

        public string Description => $"{_first.Description} → {_next.Description}";

        public ComposedSortMent(ISortMent<TSource, TMiddle> first, ISortMent<TMiddle, TTarget> next)
        {
            _first = first ?? throw new ArgumentNullException(nameof(first));
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public TTarget Morph(TSource source)
        {
            var middle = _first.Morph(source);
            return _next.Morph(middle);
        }
    }
}
