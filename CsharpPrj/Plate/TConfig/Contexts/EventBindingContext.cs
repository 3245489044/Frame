
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TConfig.ContextFrame.Abstractions;

namespace TConfig
{
    public class EventBindingContext : IContext
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


        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _cts.Cancel();
            _cts.Dispose();
            _data.Clear();
            _services.Clear();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }


        public List<BindingResult> BindingResults { get; } = new();

        public ErrorCollector ErrorCollector { get; } = new();

        public string? CurrentControlName { get; set; }

        public string? CurrentEventName { get; set; }

        public DateTime StartTime { get; }

        public DateTime? EndTime { get; set; }

        public bool IsCompleted => EndTime.HasValue;

        public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;


        public List<ControlConfig>? CurrentControls { get; set; }

        public ControlConfig? CurrentControlConfig { get; set; }

        public EventBindingDeclaration<IReadOnlyList<ParameterBinding>>? CurrentDeclaration { get; set; }

        public object? CurrentControl { get; set; }

        public object? SearchRoot { get; set; }

        public object? TargetObject { get; set; }

        public UIConfiguration? Config { get; set; }

        public IEventBus? EventBus { get; set; }

        public bool SkipCurrentControl { get; set; }

        public bool SkipPipeline { get; set; }

        public IReadOnlyList<ParameterBinding>? CurrentParameterBindings { get; set; }

        public string? CurrentScopePath { get; set; }

        public string? CurrentOutput { get; set; }

        public object? CurrentView { get; set; }

        public bool IsScriptExecutionMode { get; set; }

        public List<ControlBindingInfo> BoundControls { get; } = new();

        public List<ControlBindingInfo> SkippedControls { get; } = new();

        public int BoundCount => BoundControls.Count;

        public int SkippedCount => SkippedControls.Count;

        public Dictionary<string, WorkflowDefinitionEntry>? WorkflowDefinitions { get; set; }


        public class ContextSnapshot
        {
            public ControlConfig? CurrentControlConfig { get; set; }
            public EventBindingDeclaration<IReadOnlyList<ParameterBinding>>? CurrentDeclaration { get; set; }
            public object? CurrentControl { get; set; }
            public object? SearchRoot { get; set; }
            public object? TargetObject { get; set; }
            public UIConfiguration? Config { get; set; }
            public IEventBus? EventBus { get; set; }
            public bool SkipCurrentControl { get; set; }
            public IReadOnlyList<ParameterBinding>? CurrentParameterBindings { get; set; }
            public string? CurrentOutput { get; set; }
            public object? CurrentView { get; set; }
            public bool IsScriptExecutionMode { get; set; }
        }

        public ContextSnapshot CreateSnapshot()
        {
            return new ContextSnapshot
            {
                CurrentControlConfig = CurrentControlConfig,
                CurrentDeclaration = CurrentDeclaration,
                CurrentControl = CurrentControl,
                SearchRoot = SearchRoot,
                TargetObject = TargetObject,
                Config = Config,
                EventBus = EventBus,
                SkipCurrentControl = SkipCurrentControl,
                CurrentParameterBindings = CurrentParameterBindings,
                CurrentOutput = CurrentOutput,
                CurrentView = CurrentView,
                IsScriptExecutionMode = IsScriptExecutionMode
            };
        }

        public void RestoreSnapshot(ContextSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            CurrentControlConfig = snapshot.CurrentControlConfig;
            CurrentDeclaration = snapshot.CurrentDeclaration;
            CurrentControl = snapshot.CurrentControl;
            SearchRoot = snapshot.SearchRoot;
            TargetObject = snapshot.TargetObject;
            Config = snapshot.Config;
            EventBus = snapshot.EventBus;
            SkipCurrentControl = snapshot.SkipCurrentControl;
            CurrentParameterBindings = snapshot.CurrentParameterBindings;
            CurrentOutput = snapshot.CurrentOutput;
            CurrentView = snapshot.CurrentView;
            IsScriptExecutionMode = snapshot.IsScriptExecutionMode;
        }


        public void AddBoundControl(ControlBindingInfo info)
        {
            if (info != null)
                BoundControls.Add(info);
        }

        public void AddSkippedControl(ControlBindingInfo info)
        {
            if (info != null)
                SkippedControls.Add(info);
        }


        public void ApplyOptions(EventBindingOptions options)
        {
            if (options == null) return;

            if (options.SearchRoot != null)
                SearchRoot = options.SearchRoot;

            if (options.EventBus != null)
                EventBus = options.EventBus;

            IsScriptExecutionMode = options.ScriptExecutionEnabled;
        }

        public EventBindingContext()
        {
            StartTime = DateTime.UtcNow;
        }

        public void AddBindingResult(BindingResult result)
        {
            BindingResults.Add(result);
        }

        public void Complete()
        {
            EndTime = DateTime.UtcNow;
        }

        public string GetSummary()
        {
            var total = BindingResults.Count;
            var success = BindingResults.Count(r => r.IsSuccess);
            var failed = total - success;
            var errors = ErrorCollector.Count;

            return $"Binding completed: {success}/{total} succeeded, {errors} errors";
        }
    }
}
