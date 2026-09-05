
namespace SkeletonFlow
{
#nullable disable
    using TConfig.ContextFrame.Abstractions;
    using BasicMethod.ContextFrame.Core;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;

    #region Core
    public interface ISkeleton { }

    public class SkeletonContext : IDisposable
    {
        private readonly IContext _context;
        private readonly bool _ownsContext;
        private volatile CancellationTokenSource _linkedCts;
        private readonly List<CancellationTokenSource> _allCts = new();
        private readonly ConcurrentDictionary<string, object> _items;
        private readonly object _lock = new();
        private bool _disposed;
        private const string ItemsKey = "__SkeletonContextItems";
        private IServiceProvider _serviceProvider;

        private readonly Dictionary<Type, object> _services = new();

        public IContext Context => _context;
        public IServiceProvider ServiceProvider { get => _serviceProvider; set => _serviceProvider = value; }
        public IReadOnlyDictionary<string, object> Items => _items;

        public void RegisterService<T>(T service) where T : class
        {
            _services[typeof(T)] = service;
        }

        public void RegisterServices(IReadOnlyDictionary<Type, object> services)
        {
            foreach (var kvp in services)
                _services[kvp.Key] = kvp.Value;
        }

        public T? ResolveService<T>() where T : class
        {
            return _services.TryGetValue(typeof(T), out var service)
                ? service as T
                : null;
        }

        public T ResolveRequiredService<T>() where T : class
        {
            return ResolveService<T>()
                ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not registered in SkeletonContext");
        }

        public void SetItem(string key, object value) => _items[key] = value;

        public bool RemoveItem(string key) => _items.TryRemove(key, out _);

        public CancellationToken CancellationToken
        {
            get => _linkedCts.Token;
            set
            {
                lock (_lock)
                {
                    if (value == _linkedCts.Token) return;
                    if (_disposed) throw new ObjectDisposedException(nameof(SkeletonContext));
                    var newCts = CancellationTokenSource.CreateLinkedTokenSource(_context.CancellationToken, value);
                    _allCts.Add(newCts);
                    var oldCts = Interlocked.Exchange(ref _linkedCts, newCts);
                    oldCts?.Dispose();
                }
            }
        }

        public SkeletonContext(IContext context = null)
        {
            _context = context ?? new ContextBase();
            _ownsContext = context == null;
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_context.CancellationToken);
            _allCts.Add(_linkedCts);

            if (_context.TryGetData<ConcurrentDictionary<string, object>>(out var existing, ItemsKey))
            {
                _items = existing;
            }
            else if (_context.ContainsData<object>(ItemsKey))
            {
                throw new InvalidOperationException($"Context already has data with key '{ItemsKey}' but of incompatible type.");
            }
            else
            {
                _items = new ConcurrentDictionary<string, object>();
                _context.SetData(_items, ItemsKey);
            }
        }

        public T? GetData<T>(string key = null) where T : class => _context.GetData<T>(key);
        public bool TryGetData<T>(out T data, string key = null) where T : class => _context.TryGetData(out data, key);
        public IEnumerable<T> GetAllData<T>() where T : class => _context.GetAllData<T>();
        public void SetData<T>(T data, string key = null) where T : class => _context.SetData(data, key);
        public void RemoveData<T>(string key = null) where T : class => _context.RemoveData<T>(key);

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                lock (_lock)
                {
                    _allCts.ForEach(cts => cts.Dispose());
                    _allCts.Clear();
                    if (_ownsContext) _context.Dispose();
                }
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

   
    }

    public interface ISkeleton<in TInput, TOutput> : ISkeleton
    {
        string Name { get; }
        string Description { get; }
        TOutput Execute(TInput input, SkeletonContext context = null);
        Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context = null);
    }

    public abstract class SkeletonBase<TInput, TOutput> : ISkeleton<TInput, TOutput>
    {
        public string Name { get; }
        public string Description { get; }
        protected SkeletonBase(string name = null, string description = null) => (Name, Description) = (name ?? GetType().Name, description);

        public TOutput Execute(TInput input, SkeletonContext context = null)
        {
            var ownsContext = context == null;
            var ctx = context ?? new SkeletonContext();
            try
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return ExecuteCore(input, ctx);
            }
            finally
            {
                if (ownsContext) ctx.Dispose();
            }
        }

        public async Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context = null)
        {
            var ownsContext = context == null;
            var ctx = context ?? new SkeletonContext();
            try
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return await ExecuteCoreAsync(input, ctx).ConfigureAwait(false);
            }
            finally
            {
                if (ownsContext) ctx.Dispose();
            }
        }

        protected abstract TOutput ExecuteCore(TInput input, SkeletonContext context);
        protected abstract Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext context);
    }

    public abstract class SyncSkeletonBase<TInput, TOutput> : SkeletonBase<TInput, TOutput>
    {
        protected SyncSkeletonBase(string name = null, string description = null) : base(name, description) { }

        protected override Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext context)
        {
            return Task.FromResult(ExecuteCore(input, context));
        }
    }

    public class DelegateSkeleton<TInput, TOutput> : SkeletonBase<TInput, TOutput>
    {
        private readonly Func<TInput, SkeletonContext, TOutput> _sync;
        private readonly Func<TInput, SkeletonContext, Task<TOutput>> _async;

        public DelegateSkeleton(string name,
            Func<TInput, TOutput> sync = null,
            Func<TInput, Task<TOutput>> async = null,
            string description = null) : this(name,
                sync == null ? null : (i, _) => sync(i),
                async == null ? null : (i, _) => async(i),
                description)
        { }

        public DelegateSkeleton(string name,
            Func<TInput, SkeletonContext, TOutput> syncWithCtx = null,
            Func<TInput, SkeletonContext, Task<TOutput>> asyncWithCtx = null,
            string description = null) : base(name, description) => (_sync, _async) = (syncWithCtx, asyncWithCtx);

        protected override TOutput ExecuteCore(TInput input, SkeletonContext ctx) =>
            _sync != null ? _sync(input, ctx) :
            _async != null ? throw new NotSupportedException("同步执行需要提供同步委托，当前只有异步委托。请提供同步委托或使用 ExecuteAsync。") :
            throw new NotSupportedException("没有提供任何执行委托。");

        protected override Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext ctx) =>
            _async != null ? _async(input, ctx) :
            Task.FromResult(ExecuteCore(input, ctx));
    }
    #endregion

    #region BuiltIn
    public static class BuiltInSkeletons
    {
        private static ISkeleton<TInput, TOutput> Create<TInput, TOutput>(string name, string desc,
            Func<TInput, SkeletonContext, TOutput> sync) =>
            new DelegateSkeleton<TInput, TOutput>(name, sync, (i, ctx) => Task.FromResult(sync(i, ctx)), desc);

        public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Filter<T>(string name, Func<T, bool> predicate, string desc = null) =>
            Create<IEnumerable<T>, IEnumerable<T>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                var list = new List<T>();
                foreach (var i in src) { ctx.CancellationToken.ThrowIfCancellationRequested(); if (predicate(i)) list.Add(i); }
                return list;
            });

        public static ISkeleton<IEnumerable<T>, IEnumerable<TResult>> Map<T, TResult>(string name, Func<T, TResult> selector, string desc = null) =>
            Create<IEnumerable<T>, IEnumerable<TResult>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                var list = new List<TResult>();
                foreach (var i in src) { ctx.CancellationToken.ThrowIfCancellationRequested(); list.Add(selector(i)); }
                return list;
            });

        public static ISkeleton<IEnumerable<T>, TAccumulate> Reduce<T, TAccumulate>(string name, TAccumulate seed, Func<TAccumulate, T, TAccumulate> acc, string desc = null) =>
            Create<IEnumerable<T>, TAccumulate>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                var r = seed;
                foreach (var i in src) { ctx.CancellationToken.ThrowIfCancellationRequested(); r = acc(r, i); }
                return r;
            });

        public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Skip<T>(string name, int count, string desc = null) =>
            Create<IEnumerable<T>, IEnumerable<T>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                var list = new List<T>();
                using var enumerator = src.GetEnumerator();
                int skipped = 0;
                while (skipped < count && enumerator.MoveNext()) { ctx.CancellationToken.ThrowIfCancellationRequested(); skipped++; }
                while (enumerator.MoveNext()) { ctx.CancellationToken.ThrowIfCancellationRequested(); list.Add(enumerator.Current); }
                return list;
            });

        public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Take<T>(string name, int count, string desc = null) =>
            Create<IEnumerable<T>, IEnumerable<T>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                var list = new List<T>();
                using var enumerator = src.GetEnumerator();
                int taken = 0;
                while (taken < count && enumerator.MoveNext()) { ctx.CancellationToken.ThrowIfCancellationRequested(); list.Add(enumerator.Current); taken++; }
                return list;
            });

        public static ISkeleton<IEnumerable<T>, IEnumerable<T>> Distinct<T>(string name, IEqualityComparer<T> comparer = null, string desc = null) =>
            Create<IEnumerable<T>, IEnumerable<T>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                var result = new List<T>();
                var seen = new HashSet<T>(comparer);
                foreach (var item in src) { ctx.CancellationToken.ThrowIfCancellationRequested(); if (seen.Add(item)) result.Add(item); }
                return result;
            });

        public static ISkeleton<IEnumerable<T>, IEnumerable<IGrouping<TKey, T>>> GroupBy<T, TKey>(string name, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer = null, string desc = null) =>
            Create<IEnumerable<T>, IEnumerable<IGrouping<TKey, T>>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                var lookup = new Dictionary<TKey, List<T>>(comparer);
                foreach (var item in src)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    var key = keySelector(item);
                    if (!lookup.TryGetValue(key, out var list)) lookup[key] = list = new List<T>();
                    list.Add(item);
                }
                return lookup.Select(kvp => (IGrouping<TKey, T>)new Grouping<TKey, T>(kvp.Key, kvp.Value)).ToList();
            });

        private class Grouping<TKey, TElement> : IGrouping<TKey, TElement>
        {
            private readonly List<TElement> _elements;
            public TKey Key { get; }
            public Grouping(TKey key, List<TElement> elements) { Key = key; _elements = elements; }
            public IEnumerator<TElement> GetEnumerator() => _elements.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public static ISkeleton<IEnumerable<T>, IOrderedEnumerable<T>> OrderBy<T, TKey>(string name, Func<T, TKey> keySelector, bool descending = false, IComparer<TKey> comparer = null, string desc = null) =>
            Create<IEnumerable<T>, IOrderedEnumerable<T>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return descending ? src.ToList().OrderByDescending(keySelector, comparer) as IOrderedEnumerable<T>
                                  : src.ToList().OrderBy(keySelector, comparer) as IOrderedEnumerable<T>;
            });

        public static ISkeleton<IEnumerable<T>, T> First<T>(string name, Func<T, bool> predicate = null, string desc = null) =>
            Create<IEnumerable<T>, T>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                foreach (var item in src)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    if (predicate == null || predicate(item)) return item;
                }
                throw new InvalidOperationException("Sequence contains no matching element.");
            });

        public static ISkeleton<IEnumerable<T>, T> Single<T>(string name, Func<T, bool> predicate = null, string desc = null) =>
            Create<IEnumerable<T>, T>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                T result = default;
                bool found = false;
                foreach (var item in src)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    if (predicate == null || predicate(item))
                    {
                        if (found) throw new InvalidOperationException("Sequence contains more than one matching element.");
                        result = item;
                        found = true;
                    }
                }
                return found ? result : throw new InvalidOperationException("Sequence contains no matching element.");
            });

        public static ISkeleton<IEnumerable<T>, List<T>> ToList<T>(string name, string desc = null) =>
            Create<IEnumerable<T>, List<T>>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                return src.ToList();
            });

        public static ISkeleton<IEnumerable<T>, T[]> ToArray<T>(string name, string desc = null) =>
            Create<IEnumerable<T>, T[]>(name, desc, (src, ctx) =>
            {
                if (src == null) throw new ArgumentNullException(nameof(src));
                return src.ToArray();
            });

        public static ISkeleton<IEnumerable<T>, IEnumerable<T>> ParallelForEach<T>(string name, Func<T, T> transform, int dop = -1, string desc = null) =>
            new DelegateSkeleton<IEnumerable<T>, IEnumerable<T>>(name,
                syncWithCtx: (src, ctx) =>
                {
                    if (src == null) throw new ArgumentNullException(nameof(src));
                    var list = src.ToList();
                    var results = new T[list.Count];
                    Parallel.For(0, list.Count, new ParallelOptions { CancellationToken = ctx.CancellationToken, MaxDegreeOfParallelism = dop }, i => results[i] = transform(list[i]));
                    return results;
                },
                asyncWithCtx: async (src, ctx) =>
                {
                    if (src == null) throw new ArgumentNullException(nameof(src));
                    var list = src.ToList();
                    var results = new T[list.Count];
                    await Task.Run(() => Parallel.For(0, list.Count, new ParallelOptions { CancellationToken = ctx.CancellationToken, MaxDegreeOfParallelism = dop }, i => results[i] = transform(list[i])), ctx.CancellationToken).ConfigureAwait(false);
                    return results;
                }, desc);
    }
    #endregion

    #region Composition
    public static class SkeletonExtensions
    {
        public static ISkeleton<TInput, TOutput> Then<TInput, TIntermediate, TOutput>(
            this ISkeleton<TInput, TIntermediate> first,
            ISkeleton<TIntermediate, TOutput> next) =>
            new DelegateSkeleton<TInput, TOutput>($"{first.Name}+{next.Name}",
                (i, ctx) => next.Execute(first.Execute(i, ctx), ctx),
                async (i, ctx) => await next.ExecuteAsync(await first.ExecuteAsync(i, ctx).ConfigureAwait(false), ctx).ConfigureAwait(false),
                $"Sequential {first.Description} & {next.Description}");

        public static ISkeleton<TInput, TOutput> Branch<TInput, TOutput>(
            this Func<TInput, bool> cond,
            ISkeleton<TInput, TOutput> trueBranch,
            ISkeleton<TInput, TOutput> falseBranch) =>
            cond == null ? throw new ArgumentNullException(nameof(cond)) :
            new DelegateSkeleton<TInput, bool>("BranchCond", (i, _) => cond(i), (i, _) => Task.FromResult(cond(i)))
                .Branch(trueBranch, falseBranch);

        public static ISkeleton<TInput, TOutput> Branch<TInput, TOutput>(
            this ISkeleton<TInput, bool> condSkel,
            ISkeleton<TInput, TOutput> trueBranch,
            ISkeleton<TInput, TOutput> falseBranch) =>
            new DelegateSkeleton<TInput, TOutput>("Branch",
                (i, ctx) => condSkel.Execute(i, ctx) ? trueBranch.Execute(i, ctx) : falseBranch.Execute(i, ctx),
                async (i, ctx) => await condSkel.ExecuteAsync(i, ctx).ConfigureAwait(false)
                    ? await trueBranch.ExecuteAsync(i, ctx) : await falseBranch.ExecuteAsync(i, ctx));

        public static ISkeleton<TInput, TInput> Loop<TInput>(
            this Func<TInput, bool> cond,
            ISkeleton<TInput, TInput> body) =>
            new DelegateSkeleton<TInput, bool>("LoopCond", (i, _) => cond(i), (i, _) => Task.FromResult(cond(i)))
                .Loop(body);

        public static ISkeleton<TInput, TInput> Loop<TInput>(
            this ISkeleton<TInput, bool> condSkel,
            ISkeleton<TInput, TInput> body) =>
            new DelegateSkeleton<TInput, TInput>("Loop",
                (i, ctx) =>
                {
                    var cur = i;
                    while (condSkel.Execute(cur, ctx)) cur = body.Execute(cur, ctx);
                    return cur;
                },
                async (i, ctx) =>
                {
                    var cur = i;
                    while (await condSkel.ExecuteAsync(cur, ctx).ConfigureAwait(false))
                        cur = await body.ExecuteAsync(cur, ctx).ConfigureAwait(false);
                    return cur;
                });

        public static ISkeleton<TInput, (TOutput1, TOutput2)> Parallel<TInput, TOutput1, TOutput2>(
         this ISkeleton<TInput, TOutput1> s1,
         ISkeleton<TInput, TOutput2> s2) =>
         new DelegateSkeleton<TInput, (TOutput1, TOutput2)>("Parallel",
             syncWithCtx: (i, ctx) =>
             {
                 TOutput1 r1 = default!;
                 TOutput2 r2 = default!;
                 System.Threading.Tasks.Parallel.Invoke(
                     new ParallelOptions { CancellationToken = ctx.CancellationToken },
                     () => r1 = s1.Execute(i, ctx),
                     () => r2 = s2.Execute(i, ctx));
                 return (r1, r2);
             },
             asyncWithCtx: async (i, ctx) =>
             {
                 var t1 = s1.ExecuteAsync(i, ctx);
                 var t2 = s2.ExecuteAsync(i, ctx);
                 await Task.WhenAll(t1, t2).ConfigureAwait(false);
                 return (t1.Result, t2.Result);
             });

        public static ISkeleton<IEnumerable<TInput>, IEnumerable<TOutput>> ForEach<TInput, TOutput>(
            this ISkeleton<TInput, TOutput> element) =>
            new DelegateSkeleton<IEnumerable<TInput>, IEnumerable<TOutput>>($"ForEach({element.Name})",
                (src, ctx) => src.Select(i => element.Execute(i, ctx)).ToList(),
                async (src, ctx) =>
                {
                    var r = new List<TOutput>();
                    foreach (var i in src) r.Add(await element.ExecuteAsync(i, ctx).ConfigureAwait(false));
                    return r;
                });

        public static ISkeleton<IEnumerable<TInput>, IEnumerable<TOutput>> SelectMany<TInput, TOutput>(
            this ISkeleton<TInput, IEnumerable<TOutput>> element) =>
            new DelegateSkeleton<IEnumerable<TInput>, IEnumerable<TOutput>>($"SelectMany({element.Name})",
                (src, ctx) => src.SelectMany(i => element.Execute(i, ctx)).ToList(),
                async (src, ctx) =>
                {
                    var r = new List<TOutput>();
                    foreach (var i in src) r.AddRange(await element.ExecuteAsync(i, ctx).ConfigureAwait(false));
                    return r;
                });
    }
    #endregion

    #region Dynamic
    public class SkeletonConfig
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public List<SkeletonConfig> Children { get; set; } = new();
    }

    public interface ICompositeSkeleton : ISkeleton
    {
        void SetChildren(IEnumerable<ISkeleton> children);
        IEnumerable<ISkeleton> GetChildren();
    }

    public interface ISkeletonRegistry
    {
        void Register<TSkeleton>() where TSkeleton : class, ISkeleton;
        void Register(string typeName, Func<object[], ISkeleton> factory);
        void RegisterType<T>() where T : ISkeleton;
        void RegisterType(string name, Type type);
        ISkeleton Resolve(SkeletonConfig config, IServiceProvider services = null);
    }

    public class DefaultSkeletonRegistry : ISkeletonRegistry
    {
        private readonly ConcurrentDictionary<string, Type> _typeCache = new();
        private readonly ConcurrentDictionary<string, Func<object[], ISkeleton>> _factories = new();
        private readonly ConcurrentDictionary<ConstructorInfo, ParameterInfo[]> _ctorParamsCache = new();
        private readonly ConcurrentDictionary<string, Type> _registeredTypes = new(StringComparer.OrdinalIgnoreCase);

        public System.Text.Json.JsonSerializerOptions JsonOptions { get; set; } = new() { PropertyNameCaseInsensitive = true };

        public void Register<TSkeleton>() where TSkeleton : class, ISkeleton =>
            Register(typeof(TSkeleton).Name, args => (TSkeleton)Activator.CreateInstance(typeof(TSkeleton), args));

        public void Register(string typeName, Func<object[], ISkeleton> factory) => _factories[typeName] = factory;

        public void RegisterType<T>() where T : ISkeleton
        {
            var type = typeof(T);
            RegisterType(type.Name, type);
            if (type.FullName != null) RegisterType(type.FullName, type);
        }

        public void RegisterType(string name, Type type)
        {
            if (!typeof(ISkeleton).IsAssignableFrom(type))
                throw new ArgumentException($"Type {type} does not implement ISkeleton.");
            _registeredTypes[name] = type;
        }

        public ISkeleton Resolve(SkeletonConfig config, IServiceProvider services = null)
        {
            if (_factories.TryGetValue(config.Type, out var factory))
            {
                var paramValues = config.Parameters?.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToArray() ?? Array.Empty<object>();
                var skel = factory(paramValues);
                if (skel is ICompositeSkeleton comp && config.Children?.Count > 0)
                    comp.SetChildren(config.Children.Select(c => Resolve(c, services)));
                return skel;
            }
            var type = ResolveType(config.Type) ?? throw new InvalidOperationException($"Skeleton type '{config.Type}' not found.");
            var instance = Activator.CreateInstance(type, ResolveParams(config.Parameters, type, services)) as ISkeleton
                ?? throw new InvalidOperationException($"Type '{config.Type}' does not implement ISkeleton.");
            if (instance is ICompositeSkeleton comp2 && config.Children?.Count > 0)
                comp2.SetChildren(config.Children.Select(c => Resolve(c, services)));
            return instance;
        }

        private object[] ResolveParams(IDictionary<string, object> parameters, Type typeHint, IServiceProvider services)
        {
            if (parameters == null || parameters.Count == 0) return Array.Empty<object>();
            return ResolveConstructorParameters(typeHint, parameters, services);
        }

        private object[] ResolveConstructorParameters(Type type, IDictionary<string, object> parameters, IServiceProvider services)
        {
            var ctor = SelectBestConstructor(type, parameters, services);
            var ctorParams = _ctorParamsCache.GetOrAdd(ctor, c => c.GetParameters());
            var args = new object[ctorParams.Length];
            for (int i = 0; i < ctorParams.Length; i++)
            {
                var p = ctorParams[i];
                if (parameters.TryGetValue(p.Name, out var val))
                    args[i] = ResolveValue(val, p.ParameterType, services);
                else if (services?.GetService(p.ParameterType) is { } svc)
                    args[i] = svc;
                else if (p.HasDefaultValue)
                    args[i] = p.DefaultValue;
                else
                    throw new InvalidOperationException($"No value for parameter '{p.Name}'.");
            }
            return args;
        }

        private ConstructorInfo SelectBestConstructor(Type type, IDictionary<string, object> parameters, IServiceProvider services)
        {
            var constructors = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length);
            var candidates = new List<ConstructorInfo>();
            foreach (var ctor in constructors)
                if (TryBuildConstructorArgs(ctor, parameters, services, out _))
                    candidates.Add(ctor);
            return candidates.Count == 0
                ? throw new InvalidOperationException($"No public constructor for type {type.Name} can be satisfied with the provided parameters and services.")
                : candidates[0];
        }

        private bool TryBuildConstructorArgs(ConstructorInfo ctor, IDictionary<string, object> parameters, IServiceProvider services, out object[] args)
        {
            args = null;
            var ctorParams = _ctorParamsCache.GetOrAdd(ctor, c => c.GetParameters());
            var tempArgs = new object[ctorParams.Length];
            for (int i = 0; i < ctorParams.Length; i++)
            {
                var p = ctorParams[i];
                try
                {
                    if (parameters.TryGetValue(p.Name, out var val))
                        tempArgs[i] = ResolveValue(val, p.ParameterType, services);
                    else if (services?.GetService(p.ParameterType) is { } svc)
                        tempArgs[i] = svc;
                    else if (p.HasDefaultValue)
                        tempArgs[i] = p.DefaultValue;
                    else
                        return false;
                }
                catch (System.Text.Json.JsonException)
                {
                    throw;
                }
                catch
                {
                    return false;
                }
            }
            args = tempArgs;
            return true;
        }

        private object ResolveValue(object val, Type target, IServiceProvider services) => val switch
        {
            string s when s.StartsWith("$service:") => ResolveService(s[9..], services),
            System.Text.Json.JsonElement je => target != null
                ? System.Text.Json.JsonSerializer.Deserialize(je, target, JsonOptions)
                : ConvertJsonElementToObject(je),
            _ => val
        };

        private object ConvertJsonElementToObject(System.Text.Json.JsonElement je) => je.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => je.GetString(),
            System.Text.Json.JsonValueKind.Number => je.TryGetInt32(out int i) ? i : je.TryGetInt64(out long l) ? l : je.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Object => je.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElementToObject(p.Value)),
            System.Text.Json.JsonValueKind.Array => je.EnumerateArray().Select(ConvertJsonElementToObject).ToArray(),
            _ => je.ToString()
        };

        private object ResolveService(string serviceRef, IServiceProvider services)
        {
            var type = ResolveType(serviceRef) ?? throw new InvalidOperationException($"Service type '{serviceRef}' not found.");
            return services?.GetService(type) ?? throw new InvalidOperationException($"Service '{serviceRef}' not found.");
        }

        private Type ResolveType(string name) =>
            _typeCache.TryGetValue(name, out var t) ? t
            : _registeredTypes.TryGetValue(name, out t) ? (_typeCache[name] = t)
            : Type.GetType(name)
            ?? throw new InvalidOperationException($"Skeleton type '{name}' not registered.");
    }

    public interface IConfigParser
    {
        SkeletonConfig Parse(string configText);
    }

    public class JsonConfigParser : IConfigParser
    {
        private readonly System.Text.Json.JsonSerializerOptions _options;
        public JsonConfigParser(System.Text.Json.JsonSerializerOptions options = null) =>
            _options = options ?? new() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
        public SkeletonConfig Parse(string config) =>
            System.Text.Json.JsonSerializer.Deserialize<SkeletonConfig>(config, _options) ?? throw new InvalidOperationException("Invalid config");
    }
    #endregion

    #region Adapters
    public interface IExecutionAdapter<TEnvironment>
    {
        TInput ExtractInput<TInput>(TEnvironment env, SkeletonContext context);
        void ApplyOutput<TOutput>(TEnvironment env, TOutput output, SkeletonContext context);
        Task ApplyOutputAsync<TOutput>(TEnvironment env, TOutput output, SkeletonContext context);
    }
    #endregion

    #region Engine
    public class SkeletonEngine
    {
        private readonly ISkeletonRegistry _registry;
        private readonly IConfigParser _parser;
        public SkeletonEngine(ISkeletonRegistry registry, IConfigParser parser) => (_registry, _parser) = (registry, parser);

        public Task<TOutput> ExecuteAsync<TInput, TOutput>(string configText, TInput input, SkeletonContext context = null) =>
            ExecuteAsync<TInput, TOutput>(_parser.Parse(configText), input, context);

        public async Task<TOutput> ExecuteAsync<TInput, TOutput>(SkeletonConfig config, TInput input, SkeletonContext context = null)
        {
            context ??= new();
            var skel = _registry.Resolve(config, context.ServiceProvider) as ISkeleton<TInput, TOutput> ?? throw new InvalidOperationException("Invalid skeleton type");
            return await skel.ExecuteAsync(input, context);
        }

        public async Task ExecuteOnEnvironmentAsync<TEnvironment, TInput, TOutput>(TEnvironment env, string configText, IExecutionAdapter<TEnvironment> adapter, SkeletonContext context = null)
        {
            context ??= new();
            var output = await ExecuteAsync<TInput, TOutput>(configText, adapter.ExtractInput<TInput>(env, context), context);
            await adapter.ApplyOutputAsync(env, output, context);
        }
    }
    #endregion

    #region Composite Skeletons
    public class SequentialSkeleton<TInput, TOutput> : SkeletonBase<TInput, TOutput>, ICompositeSkeleton
    {
        private IReadOnlyList<ISkeleton> _children;
        private IReadOnlyList<Type> _childInterfaceTypes;
        private volatile bool _set;
        private readonly object _compileLock = new();

        public SequentialSkeleton(string name = null, string desc = null) : base(name ?? "Sequential", desc ?? "Sequential composition") { }

        public IEnumerable<ISkeleton> GetChildren() => _children ?? Enumerable.Empty<ISkeleton>();

        public void SetChildren(IEnumerable<ISkeleton> children)
        {
            lock (_compileLock)
            {
                if (_set) throw new InvalidOperationException("Children already set");
                var list = children?.ToList() ?? throw new ArgumentNullException(nameof(children));
                var selectedInterfaces = FindMatchingInterfaces(list, typeof(TInput), typeof(TOutput));
                _children = list.AsReadOnly();
                _childInterfaceTypes = selectedInterfaces.AsReadOnly();
                _set = true;
            }
        }

        private List<Type> FindMatchingInterfaces(List<ISkeleton> children, Type inputType, Type outputType)
        {
            var allInterfaces = children.Select(c => c.GetType().GetInterfaces()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ISkeleton<,>))
                .ToList()).ToList();

            var result = new List<Type>();
            if (!TryMatch(0, inputType, allInterfaces, result, outputType))
                throw new InvalidOperationException("Cannot find a compatible chain of interfaces for sequential composition.");
            return result;
        }

        private bool TryMatch(int index, Type requiredInput, List<List<Type>> allInterfaces, List<Type> selected, Type finalOutput)
        {
            if (index == allInterfaces.Count)
            {
                return finalOutput.IsAssignableFrom(requiredInput);
            }

            foreach (var iface in allInterfaces[index])
            {
                var genericArgs = iface.GetGenericArguments();
                var inputArg = genericArgs[0];
                var outputArg = genericArgs[1];
                if (!inputArg.IsAssignableFrom(requiredInput))
                    continue;

                if (TryMatch(index + 1, outputArg, allInterfaces, selected, finalOutput))
                {
                    selected.Insert(0, iface);
                    return true;
                }
            }
            return false;
        }

        protected override TOutput ExecuteCore(TInput input, SkeletonContext ctx)
        {
            object current = input;
            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                var iface = _childInterfaceTypes[i];
                var method = iface.GetMethod("Execute");
                current = method.Invoke(child, new[] { current, ctx });
            }
            return (TOutput)current;
        }

        protected override async Task<TOutput> ExecuteCoreAsync(TInput input, SkeletonContext ctx)
        {
            object current = input;
            for (int i = 0; i < _children.Count; i++)
            {
                var child = _children[i];
                var iface = _childInterfaceTypes[i];
                var method = iface.GetMethod("ExecuteAsync");
                var task = (Task)method.Invoke(child, new[] { current, ctx });
                await task.ConfigureAwait(false);
                var taskType = task.GetType();
                if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var resultProperty = taskType.GetProperty("Result");
                    current = resultProperty?.GetValue(task);
                }
            }
            return (TOutput)current;
        }
    }

    public class ParallelForEachSkeleton<TInput, TOutput> : SkeletonBase<IEnumerable<TInput>, IEnumerable<TOutput>>, ICompositeSkeleton
    {
        private readonly int _dop;
        private ISkeleton<TInput, TOutput> _element;
        private bool _set;

        public ParallelForEachSkeleton(int dop = -1, string name = null, string desc = null) : base(name ?? "ParallelForEach", desc ?? "Parallel for-each") => _dop = dop;

        public IEnumerable<ISkeleton> GetChildren() => _element != null ? new[] { _element } : Enumerable.Empty<ISkeleton>();

        public void SetChildren(IEnumerable<ISkeleton> children)
        {
            if (_set) throw new InvalidOperationException("Children already set");
            var list = children?.ToList() ?? throw new ArgumentNullException(nameof(children));
            _element = list.Count == 1 && list[0] is ISkeleton<TInput, TOutput> typed
                ? typed
                : throw new InvalidOperationException("Need exactly one child of correct type");
            _set = true;
        }

        protected override IEnumerable<TOutput> ExecuteCore(IEnumerable<TInput> input, SkeletonContext ctx)
        {
            var list = input.ToList();
            var results = new TOutput[list.Count];
            var dop = _dop > 0 ? _dop : -1;
            Parallel.For(0, list.Count, new ParallelOptions { CancellationToken = ctx.CancellationToken, MaxDegreeOfParallelism = dop }, i => results[i] = _element.Execute(list[i], ctx));
            return results;
        }

        protected override async Task<IEnumerable<TOutput>> ExecuteCoreAsync(IEnumerable<TInput> input, SkeletonContext ctx)
        {
            var list = input.ToList();
            var results = new TOutput[list.Count];
            if (_dop == 0)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    results[i] = await _element.ExecuteAsync(list[i], ctx).ConfigureAwait(false);
                }
            }
            else
            {
                var dop = _dop > 0 ? _dop : -1;
                using var sem = new SemaphoreSlim(dop > 0 ? dop : int.MaxValue);
                await Task.WhenAll(list.Select(async (item, i) =>
                {
                    await sem.WaitAsync(ctx.CancellationToken).ConfigureAwait(false);
                    try { ctx.CancellationToken.ThrowIfCancellationRequested(); results[i] = await _element.ExecuteAsync(item, ctx).ConfigureAwait(false); }
                    finally { sem.Release(); }
                })).ConfigureAwait(false);
            }
            return results;
        }
    }
    #endregion

    public struct Unit : IEquatable<Unit>
    {
        public static readonly Unit Default = new();
        public bool Equals(Unit other) => true;
        public override bool Equals(object obj) => obj is Unit;
        public override int GetHashCode() => 1234567;
        public static bool operator ==(Unit left, Unit right) => true;
        public static bool operator !=(Unit left, Unit right) => false;
    }
}
