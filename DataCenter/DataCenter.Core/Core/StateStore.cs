using TConfig;
using TConfig.ContextFrame.Abstractions;
using Share.StateManagement;

namespace Operation.StateBlock.Core
{
    /// <summary>
    /// 状态存储（StateStore）——基于 IStorageCell 的统一后端（2026-08-05 StateCenter 收敛；2026-09-12 迁入 DataCenter）。
    /// 字符串 KV 委托 <see cref="StorageCellStateStore"/>（同 cell Session 层），对象 KV 委托
    /// <see cref="ObjectKeyValueStore"/>（"_obj:" 前缀隔离，保持原双字典不互相覆盖语义）。
    /// StateChanged 事件经 cell.DataChanged 转发（底层唯一物理事件源）。
    /// <para>
    /// <b>分层边界</b>：本类为 DataCenter 通用件，**只承载状态 KV 能力**，不含任何业务端口
    /// （服务定位器字段如 Registry/Executor/Pipeline 属产品侧，由派生类 <c>OperationStateStore</c> 承载）。
    /// </para>
    /// </summary>
    public class StateStore : Share.StateManagement.IStateStore
    {
        /// <summary>对象态（Get&lt;T&gt;/Set&lt;T&gt;/ExtendedStates）键前缀，与字符串 KV 隔离。</summary>
        protected const string ObjectKeyPrefix = "_obj:";

        private readonly object _lock = new();
        private readonly IStorageCell _cell;
        private readonly StorageCellStateStore _stringStore;
        private readonly ObjectKeyValueStore _objectStore;
        private readonly IPublishDispatcher? _stateChangedDispatcher;

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

        /// <summary>底层分层存储单元（StateCenter 收敛目标）。</summary>
        public IPublishDispatcher? StateChangedDispatcher => _stateChangedDispatcher;

        // ── 对象 KV（带前缀隔离，委托 ObjectKeyValueStore） ──

        public IReadOnlyDictionary<string, object> ExtendedStates => _objectStore.GetAll();

        public TState? Get<TState>(string key) => _objectStore.Get<TState>(key);

        public void Set<TState>(string key, TState value) => _objectStore.Set(key, value);

        public bool Contains<TState>(string key) => _objectStore.Contains(key);

        /// <summary>仅清对象态（extendedStates），字符串 KV 保留（原语义）。派生类可覆写以级联清理扩展端口。</summary>
        public virtual void Reset()
        {
            lock (_lock)
            {
                // 对象态清理；字符串 KV 不在本方法职责内（原语义）
            }

            _objectStore.Clear();
        }

        // ── IStateStore（字符串 KV，委托 StorageCellStateStore） ──

        public void Set(string key, string? value) => _stringStore.Set(key, value);

        public string? Get(string key) => _stringStore.Get(key);

        public bool Has(string key) => _stringStore.Has(key);

        /// <summary>清字符串 KV；派生类可覆写以级联清理扩展端口。</summary>
        public virtual void Clear() => _stringStore.Clear();

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
