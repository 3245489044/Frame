using DataCenter.Abstractions.Cells;
using Operation.StateBlock.Core.Compat;
using Operation.StateBlock.Core.Storage;
using Share.StateManagement;
using TConfig.ContextFrame.Abstractions;

namespace Operation.StateBlock.Core
{
    /// <summary>
    /// 状态存储（StateStore）—— 基于端口 <see cref="IDataCell"/> 的统一后端。
    /// 字符串 KV 委托 <see cref="TierStringStore"/>（Session 层；对象键前缀隔离），对象 KV 委托 <see cref="TierObjectStore"/>；
    /// StateChanged 经数据单元变更事件转发（底层唯一物理事件源）。
    /// <para>
    /// <b>后端两态（G2 迁移期并存）</b>：端口后端（<see cref="IDataCell"/>，主路径）/ 引擎后端（IStorageCell，经 Compat 适配器接入）。
    /// 兼容构造仅为迁移存量消费方，收紧后删除（届时 Core 不再出现引擎存储契约）。
    /// </para>
    /// <para>
    /// <b>分层边界</b>：本类为 DataCenter 通用件，只承载状态 KV 能力，不含任何业务端口
    /// （服务定位器字段如 Registry/Executor/Pipeline 属产品侧，由派生类 <c>OperationStateStore</c> 承载）。
    /// </para>
    /// </summary>
    public class StateStore : Share.StateManagement.IStateStore
    {
        /// <summary>对象态（Get&lt;T&gt;/Set&lt;T&gt;/ExtendedStates）键前缀，与字符串 KV 隔离。</summary>
        protected const string ObjectKeyPrefix = "_obj:";

        private readonly IDataCell _cell;
        private readonly TierStringStore _stringStore;
        private readonly TierObjectStore _objectStore;
        private readonly IPublishDispatcher? _stateChangedDispatcher;
        private readonly IStorageCell? _legacyCell;
        private IStorageCell? _cellView;

        public event Action<string, object?>? StateChanged;

        /// <summary>端口后端构造（G2 主路径）。</summary>
        public StateStore(IDataCell cell, IPublishDispatcher? stateChangedDispatcher = null)
        {
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));
            _stringStore = new TierStringStore(_cell, ObjectKeyPrefix);
            _objectStore = new TierObjectStore(_cell, ObjectKeyPrefix);
            _stateChangedDispatcher = stateChangedDispatcher;
            _cell.DataChanged += OnCellDataChanged;
        }

        /// <summary>引擎后端构造（迁移期兼容：IStorageCell 经 <see cref="StorageCellAdapter"/> 接入端口）。</summary>
        public StateStore(IStorageCell cell, IPublishDispatcher? stateChangedDispatcher = null)
            : this(new StorageCellAdapter(cell ?? throw new ArgumentNullException(nameof(cell))), stateChangedDispatcher)
        {
            _legacyCell = cell;
        }

        /// <summary>端口数据单元（Core 内部真源）。</summary>
        public IDataCell DataCell => _cell;

        /// <summary>统一存储后端：兼容后端直返原实例（零语义漂移）；端口后端返回契约视图。</summary>
        public IStorageCell Cell => _legacyCell ?? (_cellView ??= new DataCellStorageView(_cell));

        /// <summary>StateChanged 出队派发器（可注入、默认内联零回归）。</summary>
        public IPublishDispatcher? StateChangedDispatcher => _stateChangedDispatcher;

        // ── 对象 KV（带前缀隔离，委托 TierObjectStore） ──

        public IReadOnlyDictionary<string, object> ExtendedStates => _objectStore.GetAll();

        public TState? Get<TState>(string key) => _objectStore.Get<TState>(key);

        public void Set<TState>(string key, TState value) => _objectStore.Set(key, value);

        public bool Contains<TState>(string key) => _objectStore.Contains(key);

        /// <summary>
        /// 仅清对象态（extendedStates），字符串 KV 保留（原语义）。
        /// 更名（F-28②）：与 <see cref="StateCenter.Reset"/>（清整层 Session）区分，消除同名两义。
        /// 派生类可覆写以级联清理扩展端口。
        /// </summary>
        public virtual void ResetObjectState()
        {
            _objectStore.Clear();
        }

        // ── IStateStore（字符串 KV，委托 TierStringStore） ──

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

        private void OnCellDataChanged(object? sender, DataCellChangedEventArgs e)
        {
            // 写路径解耦（可注入、默认内联零回归）：配置了派发器 → StateChanged 经派发器出队交付，
            // 写线程不执行订阅回调；null = 内联同步。
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
