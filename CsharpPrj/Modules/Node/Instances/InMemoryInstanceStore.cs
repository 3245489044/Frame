using Modules.Node.Carriers;
using Modules.Node.Projection;

namespace Modules.Node.Instances;

/// <summary>
/// 内存单文件承载（G-2 基准实现）—— 测试/开发用，同时是 I1–I6 双实现契约测试的**基准靶**
/// （对齐 <c>Carriers\InMemoryNodeCarrier</c> 既有先例）。
/// <para>
/// 语义要点：① 事务边界 = 本实例（未提交写入仅本句柄可见，I6）；② 封存后写 ⇒ <c>Rejected</c> 且阶段不变（I3）；
/// ③ 身份↔定位反解往返恒等（I1）；④ 同实例重复打开幂等、异实例 fail-fast（I2）。
/// </para>
/// <para>定位形态：<c>mem://{storeKey}/{instanceId}</c>（唯一实现 <see cref="DefaultLocate"/>，禁第二套拼法）。</para>
/// </summary>
public sealed class InMemoryInstanceStore : IInstanceStore
{
    private const string Scheme = "mem://";

    private readonly Dictionary<(InstanceStoreContainer Container, string Key), InstanceStoreRecord> _records = new();
    private readonly object _sync = new();
    private Dictionary<(InstanceStoreContainer Container, string Key), InstanceStoreRecord>? _pending;
    private string? _instanceId;
    private InstanceStage _stage = InstanceStage.Created;

    public InMemoryInstanceStore(string storeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeKey);
        StoreKey = storeKey;
    }

    public string StoreKey { get; }

    /// <summary>承载定位（未打开 ⇒ 键级定位；打开后 = 该实例定位）。</summary>
    public string Path => _instanceId is null ? $"{Scheme}{StoreKey}/" : DefaultLocate(StoreKey, _instanceId);

    public InstanceStage Stage
    {
        get
        {
            lock (_sync)
            {
                return _stage;
            }
        }
    }

    /// <summary>已提交记录数（不含未提交写入；诊断 / 契约测试用）。</summary>
    public int RecordCount
    {
        get
        {
            lock (_sync)
            {
                return _records.Count;
            }
        }
    }

    /// <summary>是否存在未提交事务（诊断：未提交时写入不应可见）。</summary>
    public bool HasPendingUnitOfWork
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null;
            }
        }
    }

    /// <summary>默认定位形态（**唯一实现**：身份→承载定位的单源拼法，禁各处手拼）。</summary>
    public static string DefaultLocate(string storeKey, string instanceId) => $"{Scheme}{storeKey}/{instanceId}";

    public string Locate(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return DefaultLocate(StoreKey, instanceId);
    }

    public bool TryResolveInstanceId(string location, out string instanceId)
    {
        instanceId = string.Empty;
        if (string.IsNullOrWhiteSpace(location))
            return false;

        var prefix = $"{Scheme}{StoreKey}/";
        if (!location.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var id = location[prefix.Length..];
        if (string.IsNullOrWhiteSpace(id))
            return false;

        instanceId = id;
        return true;
    }

    public Task OpenAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        lock (_sync)
        {
            if (_instanceId is not null)
            {
                if (!string.Equals(_instanceId, instanceId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"承载 '{StoreKey}' 已打开实例 '{_instanceId}'，禁止改开 '{instanceId}'（一实例一承载，I2）。");

                return Task.CompletedTask;
            }

            if (_stage is InstanceStage.Sealed or InstanceStage.Archived or InstanceStage.Disposed)
                throw new InvalidOperationException($"承载 '{StoreKey}' 处于 {_stage}，禁止打开（I3：封存后只读）。");

            _instanceId = instanceId;
            _stage = InstanceStage.Open;
            return Task.CompletedTask;
        }
    }

    public Task SealAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _stage = InstanceLifecycle.Next(_stage, InstanceStage.Sealed);
            return Task.CompletedTask;
        }
    }

    public Task ArchiveAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _stage = InstanceLifecycle.Next(_stage, InstanceStage.Archived);
            return Task.CompletedTask;
        }
    }

    /// <summary>进入活动阶段（装配完成、开始被消费；仍可写）。</summary>
    public Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _stage = InstanceLifecycle.Next(_stage, InstanceStage.Active);
            return Task.CompletedTask;
        }
    }

    public Task<IInstanceUnitOfWork> BeginUnitOfWorkAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_pending is not null)
                throw new InvalidOperationException($"承载 '{StoreKey}' 已有进行中的事务（事务边界不可嵌套，I6）。");

            _pending = new Dictionary<(InstanceStoreContainer, string), InstanceStoreRecord>();
            return Task.FromResult<IInstanceUnitOfWork>(new UnitOfWork(this));
        }
    }

    public Task<InstanceStoreRecord?> ReadAsync(
        InstanceStoreContainer container, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            // read-your-writes：未提交写入对本句柄可见（提交才对外生效，I6）。
            if (_pending is not null && _pending.TryGetValue((container, key), out var pending))
                return Task.FromResult<InstanceStoreRecord?>(pending);

            return Task.FromResult(_records.TryGetValue((container, key), out var record) ? record : null);
        }
    }

    public Task<CarrierWriteResult> UpsertAsync(
        InstanceStoreContainer container, string key, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(json);

        lock (_sync)
        {
            if (!InstanceLifecycle.IsWritable(_stage))
                return Task.FromResult(CarrierWriteResult.Rejected($"instance-{_stage.ToString().ToLowerInvariant()}:read-only"));

            var digest = NodeJson.Sha256Hex(json);
            var target = _pending ?? _records;
            if (target.TryGetValue((container, key), out var existing) &&
                string.Equals(existing.Digest, digest, StringComparison.Ordinal))
            {
                return Task.FromResult(CarrierWriteResult.Unchanged("same-digest"));
            }

            target[(container, key)] = new InstanceStoreRecord(container, key, json, digest, DateTimeOffset.UtcNow);
            return Task.FromResult(CarrierWriteResult.Written());
        }
    }

    public Task<IReadOnlyList<string>> EnumerateAsync(
        InstanceStoreContainer container, string? keyPrefix = null, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in _records.Keys)
            {
                if (entry.Container == container)
                    keys.Add(entry.Key);
            }

            if (_pending is not null)
            {
                foreach (var entry in _pending.Keys)
                {
                    if (entry.Container == container)
                        keys.Add(entry.Key);
                }
            }

            IReadOnlyList<string> ordered = keys
                .Where(key => keyPrefix is null || key.StartsWith(keyPrefix, StringComparison.Ordinal))
                .OrderBy(static key => key, NodeKeyOrdinalComparer.Instance)
                .ToArray();
            return Task.FromResult(ordered);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _pending = null;
            _records.Clear();
            _stage = InstanceStage.Disposed;
        }

        return ValueTask.CompletedTask;
    }

    private void CommitPending()
    {
        lock (_sync)
        {
            if (_pending is null)
                return;

            foreach (var entry in _pending)
                _records[entry.Key] = entry.Value;

            _pending = null;
        }
    }

    private void DiscardPending()
    {
        lock (_sync)
        {
            _pending = null;
        }
    }

    /// <summary>事务句柄（未提交写入落 <c>_pending</c>；回滚/未提交即释放 = 丢弃，不隐式提交）。</summary>
    private sealed class UnitOfWork : IInstanceUnitOfWork
    {
        private readonly InMemoryInstanceStore _store;
        private bool _settled;

        public UnitOfWork(InMemoryInstanceStore store) => _store = store;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (!_settled)
            {
                _store.CommitPending();
                _settled = true;
            }

            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (!_settled)
            {
                _store.DiscardPending();
                _settled = true;
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            // 未显式提交即释放 = 回滚（事务不隐式提交，I6）。
            if (!_settled)
            {
                _store.DiscardPending();
                _settled = true;
            }

            return ValueTask.CompletedTask;
        }
    }
}

