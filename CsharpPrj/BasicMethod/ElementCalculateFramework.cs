using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkeletonFlow;

namespace Framework
{
    #region 核心基础设施









    #endregion

    #region 基础设施服务

    public class Infrastructure
    {
        private readonly Common.ILockManager _lockManager;
        private readonly Common.ICacheService _cacheService;
        private readonly Common.IFactoryManager _factoryManager;

        internal Infrastructure()
        {
            _lockManager = new Common.ReaderWriterLockManager();
            _cacheService = new Common.InMemoryCacheService();
            _factoryManager = new Common.DefaultFactoryManager();
        }

        public Common.ILockManager LockManager => _lockManager;

        public Common.ICacheService Cache => _cacheService;

        public Common.IFactoryManager Factory => _factoryManager;

        public IDisposable EnterReadLock() => _lockManager.EnterReadLock();

        public IDisposable EnterWriteLock() => _lockManager.EnterWriteLock();

        public IDisposable EnterUpgradeableReadLock() => _lockManager.EnterUpgradeableReadLock();

        public T? GetCache<T>(string key) => _cacheService.Get<T>(key);

        public void SetCache<T>(string key, T value) => _cacheService.Set(key, value);

        public bool TryGetCache<T>(string key, out T? value) => _cacheService.TryGet(key, out value);

        public bool RemoveCache(string key) => _cacheService.Remove(key);

        public void ClearCache() => _cacheService.Clear();

        public int CacheCount => _cacheService.Count;
    }

    #endregion

    #region 事务服务

    public static class TransactionService
    {
        public static void Execute(Action<Transactions.ITransaction> action)
        {
            Transactions.TransactionExtensions.ExecuteInTransaction(action);
        }

        public static Task ExecuteAsync(Func<Transactions.ITransaction, Task> action)
        {
            return Transactions.TransactionExtensions.ExecuteInTransactionAsync(action);
        }

        public static Transactions.Transaction Create() => new Transactions.Transaction();

        public static Transactions.Transaction? Current => Transactions.Transaction.Current;
    }

    #endregion














    #region 工作流服务

    public class WorkflowService
    {
        private readonly SkeletonFlow.ISkeletonRegistry _registry;
        private readonly SkeletonFlow.SkeletonEngine _engine;

        internal WorkflowService()
        {
            _registry = new SkeletonFlow.DefaultSkeletonRegistry();
            _engine = new SkeletonFlow.SkeletonEngine(_registry, new SkeletonFlow.JsonConfigParser());
        }

        public SkeletonFlow.ISkeletonRegistry Registry => _registry;

        public SkeletonFlow.SkeletonEngine Engine => _engine;

        #region 流创建方法

        public SkeletonFlow.App.Flow<T, T> Start<T>() => SkeletonFlow.App.Flow.Start<T>();

        public SkeletonFlow.App.Flow<TInput, TOutput> FromSkeleton<TInput, TOutput>(SkeletonFlow.ISkeleton<TInput, TOutput> skeleton)
            => SkeletonFlow.App.Flow.FromSkeleton(skeleton);

        public SkeletonFlow.App.Flow<TInput, TOutput> Load<TInput, TOutput>(string configJson, IServiceProvider services = null)
            => SkeletonFlow.App.Flow.Load<TInput, TOutput>(configJson, services);

        #endregion

        #region 内置骨架快捷方法

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IEnumerable<T>> Filter<T>(string name, Func<T, bool> predicate)
            => SkeletonFlow.BuiltInSkeletons.Filter(name, predicate);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IEnumerable<TResult>> Map<T, TResult>(string name, Func<T, TResult> selector)
            => SkeletonFlow.BuiltInSkeletons.Map(name, selector);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, TAccumulate> Reduce<T, TAccumulate>(string name, TAccumulate seed, Func<TAccumulate, T, TAccumulate> accumulator)
            => SkeletonFlow.BuiltInSkeletons.Reduce(name, seed, accumulator);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IEnumerable<T>> Skip<T>(string name, int count)
            => SkeletonFlow.BuiltInSkeletons.Skip<T>(name, count);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IEnumerable<T>> Take<T>(string name, int count)
            => SkeletonFlow.BuiltInSkeletons.Take<T>(name, count);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IEnumerable<T>> Distinct<T>(string name, IEqualityComparer<T> comparer = null)
            => SkeletonFlow.BuiltInSkeletons.Distinct(name, comparer);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IEnumerable<IGrouping<TKey, T>>> GroupBy<T, TKey>(string name, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer = null)
            => SkeletonFlow.BuiltInSkeletons.GroupBy(name, keySelector, comparer);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IOrderedEnumerable<T>> OrderBy<T, TKey>(string name, Func<T, TKey> keySelector, bool descending = false, IComparer<TKey> comparer = null)
            => SkeletonFlow.BuiltInSkeletons.OrderBy(name, keySelector, descending, comparer);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, T> First<T>(string name, Func<T, bool> predicate = null)
            => SkeletonFlow.BuiltInSkeletons.First(name, predicate);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, T> Single<T>(string name, Func<T, bool> predicate = null)
            => SkeletonFlow.BuiltInSkeletons.Single(name, predicate);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, List<T>> ToList<T>(string name)
            => SkeletonFlow.BuiltInSkeletons.ToList<T>(name);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, T[]> ToArray<T>(string name)
            => SkeletonFlow.BuiltInSkeletons.ToArray<T>(name);

        public SkeletonFlow.ISkeleton<IEnumerable<T>, IEnumerable<T>> ParallelForEach<T>(string name, Func<T, T> transform, int degreeOfParallelism = -1)
            => SkeletonFlow.BuiltInSkeletons.ParallelForEach(name, transform, degreeOfParallelism);

        #endregion

        #region 组合方法

        public SkeletonFlow.ISkeleton<TInput, TOutput> Then<TInput, TIntermediate, TOutput>(
            SkeletonFlow.ISkeleton<TInput, TIntermediate> first,
            SkeletonFlow.ISkeleton<TIntermediate, TOutput> next)
            => first.Then(next);

        public SkeletonFlow.ISkeleton<TInput, TOutput> Branch<TInput, TOutput>(
            Func<TInput, bool> condition,
            SkeletonFlow.ISkeleton<TInput, TOutput> trueBranch,
            SkeletonFlow.ISkeleton<TInput, TOutput> falseBranch)
            => condition.Branch(trueBranch, falseBranch);

        public SkeletonFlow.ISkeleton<TInput, TInput> Loop<TInput>(
            Func<TInput, bool> condition,
            SkeletonFlow.ISkeleton<TInput, TInput> body)
            => condition.Loop(body);

        public SkeletonFlow.ISkeleton<TInput, (TOutput1, TOutput2)> Parallel<TInput, TOutput1, TOutput2>(
            SkeletonFlow.ISkeleton<TInput, TOutput1> s1,
            SkeletonFlow.ISkeleton<TInput, TOutput2> s2)
            => s1.Parallel(s2);

        public SkeletonFlow.ISkeleton<IEnumerable<TInput>, IEnumerable<TOutput>> ForEach<TInput, TOutput>(
            SkeletonFlow.ISkeleton<TInput, TOutput> element)
            => element.ForEach();

        #endregion

        #region 执行方法

        public TOutput Execute<TInput, TOutput>(SkeletonFlow.ISkeleton<TInput, TOutput> skeleton, TInput input)
            => skeleton.Execute(input);

        public Task<TOutput> ExecuteAsync<TInput, TOutput>(SkeletonFlow.ISkeleton<TInput, TOutput> skeleton, TInput input)
            => skeleton.ExecuteAsync(input);

        public TOutput Execute<TInput, TOutput>(SkeletonFlow.App.Flow<TInput, TOutput> flow, TInput input)
            => flow.Execute(input);

        public Task<TOutput> ExecuteAsync<TInput, TOutput>(SkeletonFlow.App.Flow<TInput, TOutput> flow, TInput input)
            => flow.ExecuteAsync(input);

        public Task<TOutput> ExecuteFromConfigAsync<TInput, TOutput>(string configJson, TInput input)
            => _engine.ExecuteAsync<TInput, TOutput>(configJson, input);

        #endregion
    }

    #endregion

    #region 树形结构服务

    public class TreeService
    {
        private readonly Framework.Tree.DefaultLifecycleManager _lifecycleManager;

        internal TreeService()
        {
            _lifecycleManager = new Framework.Tree.DefaultLifecycleManager();
        }

        public Framework.Tree.ILifecycleManager LifecycleManager => _lifecycleManager;

        #region 节点创建与管理

        public Framework.Tree.TreeNode CreateNode(string id)
        {
            return new SimpleTreeNode { Id = id };
        }

        public TNode CreateNode<TNode>(string id) where TNode : Framework.Tree.TreeNode, new()
        {
            return new TNode { Id = id };
        }

        public void AddChild(Framework.Tree.TreeNode parent, Framework.Tree.TreeNode child)
        {
            parent.AddChild(child);
        }

        public bool RemoveChild(Framework.Tree.TreeNode parent, Framework.Tree.TreeNode child)
        {
            return parent.RemoveChild(child);
        }

        public Framework.Tree.TreeNode? FindNode(Framework.Tree.TreeNode root, string id)
        {
            return root.FindNode(id);
        }

        public List<Framework.Tree.TreeNode> FindNodes(Framework.Tree.TreeNode root, Predicate<Framework.Tree.TreeNode> predicate)
        {
            return root.FindNodes(predicate);
        }

        #endregion

        #region 组件管理

        public void AddComponent<T>(Framework.Tree.TreeNode node, T component) where T : Framework.Tree.INodeComponent
        {
            node.AddComponent(component);
        }

        public T? GetComponent<T>(Framework.Tree.TreeNode node) where T : Framework.Tree.INodeComponent
        {
            return node.GetComponent<T>();
        }

        public bool TryGetComponent<T>(Framework.Tree.TreeNode node, out T? component) where T : Framework.Tree.INodeComponent
        {
            return node.TryGetComponent(out component);
        }

        public bool RemoveComponent<T>(Framework.Tree.TreeNode node) where T : Framework.Tree.INodeComponent
        {
            return node.RemoveComponent<T>();
        }

        #endregion

        #region 生命周期管理

        public void Initialize(Framework.Tree.TreeNode node, object? context)
        {
            node.Initialize(context);
        }

        public void Create(Framework.Tree.TreeNode node, object? context)
        {
            node.Create(context);
        }

        public void Update(Framework.Tree.TreeNode node, object? context)
        {
            node.Update(context);
        }

        public void Destroy(Framework.Tree.TreeNode node, object? context)
        {
            node.Destroy(context);
        }

        public void UpdateTree(Framework.Tree.TreeNode root, object? context)
        {
            _lifecycleManager.UpdateNode(root, context);
        }

        #endregion

    }

    #endregion

 
    #region 参数解析服务


















    #endregion


    #region 锁服务（高性能锁作用域）

    public static class LockService
    {
        public static Locking.MonitorLockScope Monitor(object locker) => Locking.Lock.Monitor(locker);

        public static Locking.MonitorTryLockScope TryMonitor(object locker, int millisecondsTimeout)
            => Locking.Lock.TryMonitor(locker, millisecondsTimeout);

        public static Locking.ReadLockScope Read(System.Threading.ReaderWriterLockSlim rwLock)
            => Locking.Lock.Read(rwLock);

        public static Locking.WriteLockScope Write(System.Threading.ReaderWriterLockSlim rwLock)
            => Locking.Lock.Write(rwLock);

        public static System.Threading.ReaderWriterLockSlim CreateReaderWriterLock(System.Threading.LockRecursionPolicy recursionPolicy = System.Threading.LockRecursionPolicy.SupportsRecursion)
        {
            return new System.Threading.ReaderWriterLockSlim(recursionPolicy);
        }
    }

    #endregion

    #region 内部实现类

    internal class SimpleTreeNode : Framework.Tree.TreeNode { }

    #endregion
}
