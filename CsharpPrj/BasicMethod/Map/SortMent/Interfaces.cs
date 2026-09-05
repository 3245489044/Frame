
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.SortMent
{

    public interface INamed
    {
        string Name { get; }

        string Description { get; }
    }


    public interface ISortMent<in TSource, out TTarget> : INamed
    {
        TTarget Morph(TSource source);
    }


    public interface IAsyncSortMent<in TSource, TTarget> : INamed
    {
        Task<TTarget> MorphAsync(TSource source, CancellationToken cancellationToken = default);
    }


    public interface IClassifyMent<in TSource, TCategory, out TTarget> : ISortMent<TSource, TTarget>
    {
        TCategory Classify(TSource source);
    }


    public interface ISortableMent<in TSource, out TTarget> : ISortMent<TSource, TTarget>
    {
        int Order { get; }
    }


    public interface IGroupMent<TSource, TKey, TTarget> : ISortMent<IEnumerable<TSource>, IEnumerable<TTarget>>
    {
        Func<TSource, TKey> KeySelector { get; }
    }


    public interface IOrderedMent<in TSource, out TTarget> : ISortableMent<TSource, TTarget>
    {
        int SubOrder { get; }
    }


    public interface ISortMentCollection<TSortMent> where TSortMent : INamed
    {
        IReadOnlyList<TSortMent> Items { get; }

        int Count { get; }

        ISortMentCollection<TSortMent> Register(TSortMent item);

        ISortMentCollection<TSortMent> RegisterRange(IEnumerable<TSortMent> items);

        ISortMentCollection<TSortMent> InsertBefore(string name, TSortMent item);

        ISortMentCollection<TSortMent> InsertAfter(string name, TSortMent item);

        ISortMentCollection<TSortMent> Remove(string name);

        void Clear();
    }


    public interface ISortMentPipeline<TContext> : ISortMentCollection<ISortableMent<TContext, TContext>>
    {
        string Name { get; }

        string Description { get; }

        TContext Execute(TContext context);

        Task<TContext> ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
    }


    public interface ISortMentRegistry<TKey, TSource, TTarget>
        where TKey : notnull
    {
        void Register(TKey key, ISortMent<TSource, TTarget> sortMent);

        void RegisterOrUpdate(TKey key, ISortMent<TSource, TTarget> sortMent);

        ISortMent<TSource, TTarget> Get(TKey key);

        bool TryGet(TKey key, out ISortMent<TSource, TTarget>? sortMent);

        bool ContainsKey(TKey key);

        bool Remove(TKey key);

        void Clear();

        int Count { get; }

        IEnumerable<TKey> Keys { get; }

        TTarget Execute(TKey key, TSource source);
    }
}
