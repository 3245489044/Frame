#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExecutionFramework.Abstractions;
using Modules.Pipeline;
using Share.StateManagement;
using WorkflowExecution.Abstractions;
using WorkFlowMethod.Diagnostics;
using IStateStore = Share.StateManagement.IStateStore;
#endregion

namespace WorkflowExecution.Pipeline
{
  
    public class PipelineContext : Modules.Pipeline.PipelineContext, IStageContext, TConfig.IExecutionContext, IDisposable
    {
        private bool _disposed;
        private readonly object _lock = new();
        private CancellationTokenSource _cts;
        private Dictionary<string, object>? _parametersCache;
        private bool _parametersCacheDirty = true;
        private ObservableDictionaryWrapper? _itemsWrapper;

        private readonly ConcurrentDictionary<string, object> _sharedData = new();

        private readonly InMemoryStateStore _dataStore = new();

        public string ContextId { get; }

        public object GraphSnapshot
        {
            get => _sharedData.TryGetValue("GraphSnapshot", out var v) ? v : null;
            private set => _sharedData["GraphSnapshot"] = value;
        }

        public object Session
        {
            get => _sharedData.TryGetValue("ExecutionSession", out var v) ? v : null;
            private set => _sharedData["ExecutionSession"] = value;
        }

        public string? CurrentNodeKey { get; set; }

        public object? CurrentInput { get; set; }

        public object? CurrentOutput { get; set; }

        public string? NextNodeKey { get; set; }

        public new CancellationToken CancellationToken
        {
            get
            {
                lock (_lock)
                {
                    return _cts?.Token ?? CancellationToken.None;
                }
            }
            set
            {
                lock (_lock)
                {
                    var oldCts = _cts;
                    _cts = CancellationTokenSource.CreateLinkedTokenSource(value);
                    oldCts?.Dispose();
                }
            }
        }

        public bool IsCancellationRequested
        {
            get
            {
                lock (_lock)
                {
                    return _cts?.IsCancellationRequested ?? false;
                }
            }
        }

        public PipelineContext(string contextId = null)
            : base(default, null)
        {
            ContextId = contextId ?? $"ctx-{Guid.NewGuid():N}";
            _cts = new CancellationTokenSource();
        }

        public override void Reset()
        {
            base.Reset();
            _sharedData.Clear();
            _parametersCache = null;
            _parametersCacheDirty = true;
            CurrentNodeKey = null;
            CurrentInput = null;
            CurrentOutput = null;
            NextNodeKey = null;
        }

        public void SetGraphSnapshot(object snapshot)
        {
            GraphSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public void SetSession(object session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }


        public TSession? GetSession<TSession>() where TSession : class
            => Session as TSession;

        public TSnapshot? GetGraphSnapshot<TSnapshot>() where TSnapshot : class
            => GraphSnapshot as TSnapshot;

        public void Cancel()
        {
            lock (_lock)
            {
                _cts?.Cancel();
            }
        }

        public void ResetCancellation()
        {
            lock (_lock)
            {
                var oldCts = _cts;
                _cts = new CancellationTokenSource();
                oldCts?.Dispose();
            }
        }

        public void SetData(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("键不能为空", nameof(key));
            _sharedData[key] = value;
            _parametersCacheDirty = true;
        }

        public object GetData(string key)
        {
            _sharedData.TryGetValue(key, out var value);
            return value;
        }

        public new T GetData<T>(string key)
        {
            if (_sharedData.TryGetValue(key, out var value) && value is T typed)
                return typed;
            return default;
        }

        public bool TryGetData(string key, out object value)
        {
            return _sharedData.TryGetValue(key, out value);
        }

        public bool RemoveData(string key)
        {
            return _sharedData.TryRemove(key, out _);
        }

        private string? GetGraphSnapshotId()
        {
            if (GraphSnapshot == null) return null;
            var prop = GraphSnapshot.GetType().GetProperty("SnapshotId");
            return prop?.GetValue(GraphSnapshot) as string;
        }

        private string? GetSessionId()
        {
            if (Session == null) return null;
            var prop = Session.GetType().GetProperty("SessionId");
            return prop?.GetValue(Session) as string;
        }

        public PipelineContextSnapshot CreateSnapshot()
        {
            return new PipelineContextSnapshot
            {
                ContextId = ContextId,
                GraphSnapshotId = GetGraphSnapshotId(),
                SessionId = GetSessionId(),
                CurrentNodeKey = CurrentNodeKey,
                CurrentInput = CurrentInput,
                CurrentOutput = CurrentOutput,
                NextNodeKey = NextNodeKey,
                SharedData = new Dictionary<string, object>(_sharedData, StringComparer.OrdinalIgnoreCase)
            };
        }

        public void RestoreFromSnapshot(PipelineContextSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            CurrentNodeKey = null;
            CurrentInput = snapshot.CurrentInput;
            CurrentOutput = snapshot.CurrentOutput;
            NextNodeKey = snapshot.NextNodeKey;

            _sharedData.Clear();
            if (snapshot.SharedData != null)
            {
                foreach (var kvp in snapshot.SharedData)
                {
                    _sharedData[kvp.Key] = kvp.Value;
                }
            }
        }


        public string ExecutionId => ContextId;

        public string WorkflowId => GetSessionId() ?? ContextId;

        public string NodeKey => CurrentNodeKey;

        public IReadOnlyDictionary<string, object> Parameters
        {
            get
            {
                if (!_parametersCacheDirty && _parametersCache != null)
                    return _parametersCache;

                var parameters = new Dictionary<string, object>();
                foreach (var kvp in _sharedData)
                {
                    if (kvp.Key.StartsWith("param.", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters[kvp.Key.Substring(6)] = kvp.Value;
                    }
                }
                _parametersCache = parameters;
                _parametersCacheDirty = false;
                return parameters;
            }
        }

        public new IDictionary<string, object> Items
        {
            get
            {
                var hasWrapper = _itemsWrapper != null;
                if (!hasWrapper)
                {
                    var sd = _sharedData;
                    _itemsWrapper = new ObservableDictionaryWrapper(sd, () => _parametersCacheDirty = true);
                }
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log("WorkflowMethodExecution_Items_Getter", new
                {
                    HasWrapper = hasWrapper,
                    SdType = _sharedData?.GetType().FullName ?? "null"
                });
                return _itemsWrapper;
            }
        }

        public new IServiceProvider? ServiceProvider { get; set; }

        public T GetParameter<T>(string key)
        {
            var paramKey = $"param.{key}";
            if (_sharedData.TryGetValue(paramKey, out var value) && value is T typed)
                return typed;
            return default;
        }

        public T GetParameter<T>(int index)
        {
            var paramValues = _sharedData
                .Where(kvp => kvp.Key.StartsWith("param.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Value)
                .ToList();

            if (index >= 0 && index < paramValues.Count && paramValues[index] is T typed)
                return typed;

            throw new IndexOutOfRangeException($"Parameter at index {index} not found or type mismatch");
        }

        public bool TryGetParameter<T>(string key, out T value)
        {
            var paramKey = $"param.{key}";
            if (_sharedData.TryGetValue(paramKey, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default!;
            return false;
        }

        public bool TryGetData<T>(string key, out T value)
        {
            if (_sharedData.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default!;
            return false;
        }

        public void SetData<T>(string key, T value)
        {
            SetData(key, (object)value!);
        }

        public TConfig.IExecutionContext CreateSubContext(string? nodeKey)
        {
            var subContext = new PipelineContext($"{ContextId}.{nodeKey ?? "sub"}");
            subContext.SetGraphSnapshot(GraphSnapshot);
            subContext.SetSession(Session);
            subContext.CurrentNodeKey = nodeKey ?? CurrentNodeKey;

            foreach (var kvp in _sharedData)
            {
                subContext._sharedData[kvp.Key] = kvp.Value;
            }

            return subContext;
        }


        IStateStore IStageContext.Data => _dataStore;

        IReadOnlyList<string>? IStageContext.NodeKeys
        {
            get
            {
                if (GraphSnapshot == null) return null;
                var prop = GraphSnapshot.GetType().GetProperty("NodeKeys");
                return prop?.GetValue(GraphSnapshot) as IReadOnlyList<string>;
            }
        }

        string? IStageContext.SessionId => GetSessionId() ?? ContextId;

        void IStageContext.LogInfo(string stage, string message)
            => WorkFlowMethodLog.Log("StageContext_Info", new { stage, message });

        void IStageContext.LogWarn(string stage, string message)
            => WorkFlowMethodLog.Log("StageContext_Warn", new { stage, message });

        void IStageContext.LogError(string stage, string message, Exception? ex)
            => WorkFlowMethodLog.Log("StageContext_Error", new { stage, message, error = ex?.Message ?? "" });

        void IStageContext.LogDebug(string stage, string message)
            => WorkFlowMethodLog.Log("StageContext_Debug", new { stage, message });


        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                lock (_lock)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                }

                if (Session is IDisposable disposableSession)
                    disposableSession.Dispose();
            }

            base.Dispose(disposing);
        }

        public override string ToString()
        {
            return $"PipelineContext [Id={ContextId}, Snapshot={GetGraphSnapshotId()}, Session={GetSessionId()}]";
        }
    }

    internal class ObservableDictionaryWrapper : IDictionary<string, object>
    {
        private readonly ConcurrentDictionary<string, object> _inner;
        private readonly Action _onModified;

        public ObservableDictionaryWrapper(ConcurrentDictionary<string, object> inner, Action onModified)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _onModified = onModified ?? throw new ArgumentNullException(nameof(onModified));
        }

        public object this[string key]
        {
            get => _inner[key];
            set { _inner[key] = value; _onModified(); }
        }

        public ICollection<string> Keys => _inner.Keys;
        public ICollection<object> Values => _inner.Values;
        public int Count => _inner.Count;
        public bool IsReadOnly => false;

        public void Add(string key, object value) { _inner.TryAdd(key, value); _onModified(); }
        public void Add(KeyValuePair<string, object> item) { _inner.TryAdd(item.Key, item.Value); _onModified(); }
        public void Clear() { _inner.Clear(); _onModified(); }
        public bool Contains(KeyValuePair<string, object> item) => _inner.Contains(item);
        public bool ContainsKey(string key) => _inner.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) { }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
        public bool Remove(string key) { var result = _inner.TryRemove(key, out _); if (result) _onModified(); return result; }
        public bool Remove(KeyValuePair<string, object> item) { var result = ((IDictionary<string, object>)_inner).Remove(item); if (result) _onModified(); return result; }
        public bool TryGetValue(string key, out object value) => _inner.TryGetValue(key, out value);
    }

    public class PipelineContextSnapshot
    {
        public string ContextId { get; set; }

        public string GraphSnapshotId { get; set; }

        public string SessionId { get; set; }

        public string CurrentNodeKey { get; set; }

        public object CurrentInput { get; set; }

        public object CurrentOutput { get; set; }

        public string NextNodeKey { get; set; }

        public Dictionary<string, object> SharedData { get; set; }
    }
}
