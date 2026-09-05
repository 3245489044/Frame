
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TConfig.ContextFrame.Abstractions;

namespace TConfig
{
    public class FrameworkContext : IContext
    {

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public IContext? Parent { get; set; }

        public bool IsDisposed { get; private set; }

        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public CancellationToken CancellationToken => _cts.Token;

        private readonly CancellationTokenSource _cts = new();

        public event EventHandler<ContextDataChangedEventArgs>? DataChanged;


        private readonly Dictionary<string, object?> _data = new();

        public void SetData<T>(T value, string? key = null) where T : notnull
        {
            var resolvedKey = key ?? typeof(T).FullName ?? typeof(T).Name;
            var exists = _data.TryGetValue(resolvedKey, out var oldValue);
            _data[resolvedKey] = value;
            var changeType = exists ? DataChangeType.Updated : DataChangeType.Added;
            DataChanged?.Invoke(this, new ContextDataChangedEventArgs(resolvedKey, oldValue, value, changeType));
        }

        public T? GetData<T>(string? key = null) where T : notnull
        {
            var resolvedKey = key ?? typeof(T).FullName ?? typeof(T).Name;
            if (_data.TryGetValue(resolvedKey, out var value) && value is T t)
                return t;
            if (Parent is IContext parent)
                return parent.GetData<T>(key);
            return default;
        }

        public bool TryGetData<T>(out T? value, string? key = null) where T : notnull
        {
            var resolvedKey = key ?? typeof(T).FullName ?? typeof(T).Name;
            if (_data.TryGetValue(resolvedKey, out var raw) && raw is T t)
            {
                value = t;
                return true;
            }
            if (Parent is IContext parent)
                return parent.TryGetData(out value, key);
            value = default;
            return false;
        }

        public bool RemoveData<T>(string? key = null) where T : notnull
        {
            var resolvedKey = key ?? typeof(T).FullName ?? typeof(T).Name;
            if (_data.Remove(resolvedKey, out var oldValue))
            {
                DataChanged?.Invoke(this, new ContextDataChangedEventArgs(resolvedKey, oldValue, null, DataChangeType.Removed));
                return true;
            }
            return false;
        }

        public bool ContainsData<T>(string? key = null) where T : notnull
        {
            var resolvedKey = key ?? typeof(T).FullName ?? typeof(T).Name;
            if (_data.ContainsKey(resolvedKey))
                return true;
            return Parent?.ContainsData<T>(key) ?? false;
        }

        public IEnumerable<T> GetAllData<T>() where T : notnull
            => _data.Values.OfType<T>().ToList();

        public IEnumerable<object> GetAllData()
            => _data.Values.Where(v => v != null).ToList()!;


        private readonly Dictionary<Type, object> _services = new();

        public T GetOrAddService<T>(Func<T> factory) where T : notnull
        {
            if (_services.TryGetValue(typeof(T), out var existing) && existing is T t)
                return t;
            var instance = factory();
            _services[typeof(T)] = instance;
            return instance;
        }

        public bool TryGetService<T>(out T? service) where T : notnull
        {
            if (_services.TryGetValue(typeof(T), out var raw) && raw is T t)
            {
                service = t;
                return true;
            }
            if (Parent is IContext parent)
                return parent.TryGetService(out service);
            service = default;
            return false;
        }


        public virtual void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _cts.Cancel();
            _cts.Dispose();

            foreach (var disposable in GetAllData().OfType<IDisposable>())
            {
                disposable.Dispose();
            }

            _data.Clear();
            _services.Clear();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }


        public FrameworkOptions Options { get; }

        #region ---- 核心服务 ----

        public IEventBus? EventBus
        {
            get
            {
                TryGetService<IEventBus>(out var svc);
                return svc;
            }
            set => GetOrAddService<IEventBus>(() => value!);
        }

        public EventBindingContext? EventBindingContext
        {
            get
            {
                TryGetService<EventBindingContext>(out var svc);
                return svc;
            }
            set => GetOrAddService<EventBindingContext>(() => value!);
        }

        public IEventBindingService? EventBindingService
        {
            get
            {
                TryGetService<IEventBindingService>(out var svc);
                return svc;
            }
            set => GetOrAddService<IEventBindingService>(() => value!);
        }

        public IEventExecutionService? EventExecutionService
        {
            get
            {
                TryGetService<IEventExecutionService>(out var svc);
                return svc;
            }
            set => GetOrAddService<IEventExecutionService>(() => value!);
        }

        public IEventNameResolver? EventNameResolver
        {
            get
            {
                TryGetService<IEventNameResolver>(out var svc);
                return svc;
            }
            set => GetOrAddService<IEventNameResolver>(() => value!);
        }

        public IControlFinderStrategy? ControlFinderStrategy
        {
            get
            {
                TryGetService<IControlFinderStrategy>(out var svc);
                return svc;
            }
            set => GetOrAddService<IControlFinderStrategy>(() => value!);
        }

        public IEventBinder? EventBinder
        {
            get
            {
                TryGetService<IEventBinder>(out var svc);
                return svc;
            }
            set => GetOrAddService<IEventBinder>(() => value!);
        }

        public IEventBindingRecorder? EventBindingRecorder
        {
            get
            {
                TryGetService<IEventBindingRecorder>(out var svc);
                return svc;
            }
            set => GetOrAddService<IEventBindingRecorder>(() => value!);
        }

        public IEventHandlerFactory? EventHandlerFactory
        {
            get
            {
                TryGetService<IEventHandlerFactory>(out var svc);
                return svc;
            }
            set => GetOrAddService<IEventHandlerFactory>(() => value!);
        }

        public IUISubsystemInitializer? UISubsystemInitializer
        {
            get
            {
                TryGetService<IUISubsystemInitializer>(out var svc);
                return svc;
            }
            set => GetOrAddService<IUISubsystemInitializer>(() => value!);
        }

        public IExecutionEngine? ExecutionEngine
        {
            get
            {
                TryGetService<IExecutionEngine>(out var svc);
                return svc;
            }
            set => GetOrAddService<IExecutionEngine>(() => value!);
        }

        #endregion

        public FrameworkContext(FrameworkOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }
    }
}
