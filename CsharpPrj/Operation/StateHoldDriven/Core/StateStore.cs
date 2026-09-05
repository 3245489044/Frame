using TConfig;
using TConfig.ContextFrame.Abstractions;
using ExecutionFramework.Engine;
using WorkflowExecution.Pipeline;
using Operation.StateBlock.Abstractions;
using Share.StateManagement;
using OpStateStore = Operation.StateBlock.Abstractions.IStateStore;

namespace Operation.StateBlock.Core
{
    /// <summary>
    /// 状态存储（StateStore）——基于 IStorageCell 的统一后端（2026-08-05 StateCenter 收敛）。
    /// 字符串 KV 委托 <see cref="StorageCellStateStore"/>（同 cell Session 层），对象 KV 委托
    /// <see cref="ObjectKeyValueStore"/>（"_obj:" 前缀隔离，保持原双字典不互相覆盖语义）。
    /// StateChanged 事件经 cell.DataChanged 转发（底层唯一物理事件源）。
    /// </summary>
    public class StateStore : OpStateStore
    {
        /// <summary>对象态（Get&lt;T&gt;/Set&lt;T&gt;/ExtendedStates）键前缀，与字符串 KV 隔离。</summary>
        private const string ObjectKeyPrefix = "_obj:";

        private readonly object _lock = new();
        private readonly IStorageCell _cell;
        private readonly StorageCellStateStore _stringStore;
        private readonly ObjectKeyValueStore _objectStore;
        private volatile IPublishDispatcher? _stateChangedDispatcher;
        private ITrConfigRegistry? _registry;
        private IWorkflowRegistry? _workflowRegistry;
        private FunctionExecutor? _executor;
        private PipelineExecutor? _pipeline;

        public event Action<string, object?>? StateChanged;

        public StateStore(IStorageCell cell, IPublishDispatcher? stateChangedDispatcher = null)
        {
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));
            _stringStore = new StorageCellStateStore(_cell, ObjectKeyPrefix);
            _objectStore = new ObjectKeyValueStore(_cell, ObjectKeyPrefix);
            _stateChangedDispatcher = stateChangedDispatcher;
            _cell.DataChanged += OnCellDataChanged;
        }

        /// <summary>统一存储后端（StateCenter 收敛目标）。</summary>
        public IStorageCell Cell => _cell;

        public ITrConfigRegistry? Registry
        {
            get { lock (_lock) return _registry; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _registry = value;
            }
        }

        public IWorkflowRegistry? WorkflowRegistry
        {
            get { lock (_lock) return _workflowRegistry; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _workflowRegistry = value;
            }
        }

        public FunctionExecutor? Executor
        {
            get { lock (_lock) return _executor; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _executor = value;
            }
        }

        public PipelineExecutor? Pipeline
        {
            get { lock (_lock) return _pipeline; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock) _pipeline = value;
            }
        }

        // ── 对象 KV（带前缀隔离，委托 ObjectKeyValueStore） ──

        public IReadOnlyDictionary<string, object> ExtendedStates => _objectStore.GetAll();

        public TState? Get<TState>(string key) => _objectStore.Get<TState>(key);

        public void Set<TState>(string key, TState value) => _objectStore.Set(key, value);

        /// <summary>仅清对象态（extendedStates），字符串 KV 保留（原语义）。</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _registry = null;
                _workflowRegistry = null;
                _executor = null;
                _pipeline = null;
            }

            _objectStore.Clear();
        }

        public bool HasRegistry
        {
            get { lock (_lock) return _registry != null; }
        }

        public bool HasExecutor
        {
            get { lock (_lock) return _executor != null; }
        }

        public bool HasPipeline
        {
            get { lock (_lock) return _pipeline != null; }
        }

        public bool HasWorkflowRegistry
        {
            get { lock (_lock) return _workflowRegistry != null; }
        }

        // ── IStateStore（字符串 KV，委托 StorageCellStateStore） ──

        public void Set(string key, string? value) => _stringStore.Set(key, value);

        public string? Get(string key) => _stringStore.Get(key);

        public bool Has(string key) => _stringStore.Has(key);

        public void Clear()
        {
            lock (_lock)
            {
                _registry = null;
                _workflowRegistry = null;
                _executor = null;
                _pipeline = null;
            }

            _stringStore.Clear();
        }

        public IReadOnlyCollection<string> GetAllKeys() => _stringStore.GetAllKeys();

        public string? GetOrAdd(string key, string? defaultValue) => _stringStore.GetOrAdd(key, defaultValue);

        public void Remove(string key) => _stringStore.Remove(key);

        public void RemoveByPrefix(string prefix) => _stringStore.RemoveByPrefix(prefix);

        public void SetRange(IEnumerable<KeyValuePair<string, string?>> values) => _stringStore.SetRange(values);

        public Dictionary<string, string?> GetAll() => _stringStore.GetAll();

        public int Count => _stringStore.Count;

        public bool IsEmpty => _stringStore.IsEmpty;

        private void OnCellDataChanged(object? sender, ContextDataChangedEventArgs e)
        {
            // P4 写路径解耦（可注入、默认内联零回归）：配置了派发器 → StateChanged 经派发器出队交付，
            // 写线程不执行订阅回调（第①线广播与 hub 第②线共享同一派发器 → 两线统一出队）。null = 内联同步（现状）。
            var dispatcher = _stateChangedDispatcher;
            if (dispatcher is null)
            {
                StateChanged?.Invoke(e.Key, e.NewValue);
                return;
            }
            dispatcher.Dispatch(() => StateChanged?.Invoke(e.Key, e.NewValue));
        }
    }
}
