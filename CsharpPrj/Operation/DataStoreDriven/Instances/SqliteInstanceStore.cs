using System.Data.SQLite;
using System.Globalization;
using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Instances;
using Modules.Node.Projection;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Instances;

/// <summary>
/// SQLite 单文件承载（<see cref="IInstanceStore"/> 实现）—— 把通用实例框架落到「单库单写通道 + 单文件事务」。
/// <para>
/// 纪律（对齐框架 G-2 与 §3.5）：
/// ① 写一律经 <see cref="ISqliteWriteHost"/>（按库文件键控的单写通道，禁自建第二套写门）；
/// ② 事务一律经 <see cref="SqliteTransaction"/>（禁自建事务协议；单文件内多表提交原子，I6）；
/// ③ 同文件可承载同树多实例（按 <c>instance_id</c> 行分区），但**同实例身份不得绑到两个文件**（I2）；
/// ④ 封存后写 ⇒ <c>Rejected</c> 且阶段不变（I3）；阶段持久化于 <c>ixn_stage</c>（跨进程可续）。
/// </para>
/// <para>承载定位形态：<c>{文件路径}#{instanceId}</c>（唯一拼法，身份↔定位双向可反解，I1）。</para>
/// </summary>
public sealed class SqliteInstanceStore : IInstanceStore
{
    private const string StageTable = "ixn_stage";
    private const string LocatorSeparator = "#";

    private static readonly InstanceStoreContainer[] AllContainers =
    {
        InstanceStoreContainer.Meta,
        InstanceStoreContainer.State,
        InstanceStoreContainer.Placements,
        InstanceStoreContainer.Nodes,
        InstanceStoreContainer.Edges,
        InstanceStoreContainer.Events,
        InstanceStoreContainer.Checkpoints,
    };

    private readonly ISqliteWriteHost _host;
    private readonly SqliteConnectionFactory _factory;
    private readonly INodeDiagnosticsSink _diagnostics;
    private readonly object _sync = new();
    private string? _instanceId;
    private InstanceStage _stage = InstanceStage.Created;
    private SqliteTransaction? _activeTransaction;

    /// <param name="storeKey">承载键（= <see cref="InstanceKindSpec.StoreKind"/>）。</param>
    /// <param name="factory">连接工厂（连接串与库文件路径的单源）。</param>
    /// <param name="host">单写通道 host（**必须注入**：禁进程静态默认，I7/K5）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public SqliteInstanceStore(
        string storeKey,
        SqliteConnectionFactory factory,
        ISqliteWriteHost host,
        INodeDiagnosticsSink? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeKey);
        StoreKey = storeKey;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
        Path = string.IsNullOrWhiteSpace(factory.DatabasePath) ? factory.ConnectionString : factory.DatabasePath!;
    }

    public string StoreKey { get; }

    /// <summary>单文件承载定位（库文件路径；不可解析时退为连接串，保证身份仍可反解）。</summary>
    public string Path { get; }

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

    /// <summary>已绑定实例身份（未打开 ⇒ null）。</summary>
    public string? BoundInstanceId
    {
        get
        {
            lock (_sync)
            {
                return _instanceId;
            }
        }
    }

    /// <summary>承载定位单源拼法（<c>{文件}#{实例}</c>）。</summary>
    public string Locate(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return $"{Path}{LocatorSeparator}{instanceId}";
    }

    public bool TryResolveInstanceId(string location, out string instanceId)
    {
        instanceId = string.Empty;
        if (string.IsNullOrWhiteSpace(location))
            return false;

        var prefix = $"{Path}{LocatorSeparator}";
        if (!location.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var id = location[prefix.Length..];
        if (string.IsNullOrWhiteSpace(id))
            return false;

        instanceId = id;
        return true;
    }

    /// <summary>打开承载：绑定身份（异身份 fail-fast）+ 幂等建表 + 装载/写回阶段。</summary>
    public Task OpenAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        lock (_sync)
        {
            if (_instanceId is not null && !string.Equals(_instanceId, instanceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"承载 '{StoreKey}'（{Path}）已绑定实例 '{_instanceId}'，禁止改绑 '{instanceId}'（一实例一承载句柄，I2）。");
            }

            if (_instanceId is null)
            {
                _factory.EnsureFileCreated();
                var executor = Executor();
                executor.ExecuteNonQuery(CreateStageTableSql());
                foreach (var container in AllContainers)
                    executor.ExecuteNonQuery(CreateContainerSql(container));

                _instanceId = instanceId;
                _stage = LoadOrInitStage(executor, instanceId);
            }

            return Task.CompletedTask;
        }
    }

    public Task SealAsync(CancellationToken cancellationToken = default) => Transition(InstanceStage.Sealed);

    public Task ArchiveAsync(CancellationToken cancellationToken = default) => Transition(InstanceStage.Archived);

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _instanceId = null;
            _activeTransaction = null;
            _stage = InstanceStage.Disposed;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>阶段跃迁（非法跃迁即抛）+ 持久化（跨进程可续）。</summary>
    private Task Transition(InstanceStage target)
    {
        lock (_sync)
        {
            RequireOpen();
            _stage = InstanceLifecycle.Next(_stage, target);
            PersistStage(Executor(), _instanceId!, _stage);
        }

        _diagnostics.Report(new NodeDiagnostic(
            "instance.carrier-transitioned",
            NodeDiagnosticSeverity.Info,
            $"实例承载阶段变更（={target}）",
            $"{StoreKey}|{_instanceId}"));
        return Task.CompletedTask;
    }

    private void RequireOpen()
    {
        if (_instanceId is null)
            throw new InvalidOperationException($"承载 '{StoreKey}' 尚未打开（OpenAsync 未调用）。");

        if (_stage is InstanceStage.Disposed)
            throw new InvalidOperationException($"承载 '{StoreKey}' 已处置，禁止再变更阶段（I3）。");
    }

    /// <summary>命令执行面：事务进行中 ⇒ 该事务（本调用方写入参与原子提交）；否则 ⇒ 单写通道 autocommit。</summary>
    private IDbCommandExecutor Executor()
    {
        var active = _activeTransaction;
        return active is not null ? active.Commands : new AutocommitExecutor(_host, _factory.ConnectionString);
    }

    private InstanceStage LoadOrInitStage(IDbCommandExecutor executor, string instanceId)
    {
        var rows = executor.ExecuteQuery(
            $"SELECT stage FROM {StageTable} WHERE instance_id = @id",
            new Dictionary<string, object> { ["@id"] = instanceId });

        if (rows.Count > 0 && rows[0].TryGetValue("stage", out var value) && value is string text &&
            Enum.TryParse<InstanceStage>(text, ignoreCase: true, out var stage))
        {
            return stage;
        }

        // 首开 ⇒ Open；Active 由装配方完成后显式推进（本层不猜）。
        const InstanceStage opened = InstanceStage.Open;
        PersistStage(executor, instanceId, opened);
        return opened;
    }

    private static void PersistStage(IDbCommandExecutor executor, string instanceId, InstanceStage stage)
        => executor.ExecuteNonQuery(
            $"INSERT OR REPLACE INTO {StageTable} (instance_id, stage, updated_at) VALUES (@id, @stage, @at)",
            new Dictionary<string, object>
            {
                ["@id"] = instanceId,
                ["@stage"] = stage.ToString(),
                ["@at"] = DateTimeOffset.UtcNow.ToString("O"),
            });

    private static string TableOf(InstanceStoreContainer container)
        => "ixn_" + container.ToString().ToLowerInvariant();

    private static string CreateStageTableSql() =>
        $"CREATE TABLE IF NOT EXISTS {StageTable} (instance_id TEXT PRIMARY KEY, stage TEXT NOT NULL, updated_at TEXT NOT NULL)";

    private static string CreateContainerSql(InstanceStoreContainer container) =>
        $"CREATE TABLE IF NOT EXISTS {TableOf(container)} (" +
        "instance_id TEXT NOT NULL, key TEXT NOT NULL, json TEXT NOT NULL, digest TEXT NULL, written_at TEXT NOT NULL, " +
        "PRIMARY KEY (instance_id, key))";

    /// <summary>
    /// 开启事务边界：**同步构造** <see cref="SqliteTransaction"/>（在调用方 ExecutionContext 内登记环境事务，
    /// 避免 async 内 Enter 使事务内写路由到单写门自锁 —— 见 <see cref="SqliteTransaction"/> 类注释）。
    /// </summary>
    public Task<IInstanceUnitOfWork> BeginUnitOfWorkAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            RequireOpen();
            if (_activeTransaction is not null)
                throw new InvalidOperationException($"承载 '{StoreKey}' 已有进行中的事务（事务边界不可嵌套，I6）。");

            _activeTransaction = new SqliteTransaction(_host, _factory.ConnectionString, $"instance:{_instanceId}");
            return Task.FromResult<IInstanceUnitOfWork>(new SqliteUnitOfWork(this, _activeTransaction));
        }
    }

    public Task<InstanceStoreRecord?> ReadAsync(
        InstanceStoreContainer container, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        RequireOpen();

        var rows = Executor().ExecuteQuery(
            $"SELECT json, digest, written_at FROM {TableOf(container)} WHERE instance_id = @id AND key = @key",
            new Dictionary<string, object> { ["@id"] = _instanceId!, ["@key"] = key });

        if (rows.Count == 0)
            return Task.FromResult<InstanceStoreRecord?>(null);

        var row = rows[0];
        var json = row.TryGetValue("json", out var j) && j is not DBNull ? Convert.ToString(j) ?? string.Empty : string.Empty;
        var digest = row.TryGetValue("digest", out var d) && d is not DBNull ? Convert.ToString(d) : null;
        var writtenAt = row.TryGetValue("written_at", out var w) && w is not DBNull
            ? ParseTimestamp(Convert.ToString(w))
            : DateTimeOffset.MinValue;

        return Task.FromResult<InstanceStoreRecord?>(new InstanceStoreRecord(container, key, json, digest, writtenAt));
    }

    public Task<CarrierWriteResult> UpsertAsync(
        InstanceStoreContainer container, string key, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(json);
        RequireOpen();

        if (!InstanceLifecycle.IsWritable(_stage))
        {
            return Task.FromResult(
                CarrierWriteResult.Rejected($"instance-{_stage.ToString().ToLowerInvariant()}:read-only"));
        }

        var executor = Executor();
        var digest = InstanceDigest.Of(json);
        var table = TableOf(container);

        var existing = executor.ExecuteQuery(
            $"SELECT digest FROM {table} WHERE instance_id = @id AND key = @key",
            new Dictionary<string, object> { ["@id"] = _instanceId!, ["@key"] = key });

        if (existing.Count > 0 && string.Equals(Convert.ToString(existing[0]["digest"]), digest, StringComparison.Ordinal))
            return Task.FromResult(CarrierWriteResult.Unchanged("same-digest"));

        executor.ExecuteNonQuery(
            $"INSERT OR REPLACE INTO {table} (instance_id, key, json, digest, written_at) " +
            "VALUES (@id, @key, @json, @digest, @at)",
            new Dictionary<string, object>
            {
                ["@id"] = _instanceId!,
                ["@key"] = key,
                ["@json"] = json,
                ["@digest"] = digest,
                ["@at"] = DateTimeOffset.UtcNow.ToString("O"),
            });

        return Task.FromResult(CarrierWriteResult.Written());
    }

    public Task<IReadOnlyList<string>> EnumerateAsync(
        InstanceStoreContainer container, string? keyPrefix = null, CancellationToken cancellationToken = default)
    {
        RequireOpen();

        var parameters = new Dictionary<string, object> { ["@id"] = _instanceId! };
        var sql = $"SELECT key FROM {TableOf(container)} WHERE instance_id = @id";
        if (!string.IsNullOrEmpty(keyPrefix))
        {
            sql += " AND key LIKE @prefix ESCAPE '\\'";
            parameters["@prefix"] = keyPrefix.Replace("%", "\\%").Replace("_", "\\_") + "%";
        }

        var rows = Executor().ExecuteQuery(sql, parameters);

        // 枚举序 = 键序数序（N6；与读投影/内存实现同一比较器，禁随库枚举序漂移）。
        IReadOnlyList<string> keys = rows
            .Select(row => Convert.ToString(row["key"]) ?? string.Empty)
            .Where(static k => k.Length > 0)
            .OrderBy(static k => k, NodeKeyOrdinalComparer.Instance)
            .ToArray();
        return Task.FromResult(keys);
    }

    /// <summary>事务收尾（清空进行中事务句柄；仅当仍是本事务时）。</summary>
    private void EndUnitOfWork(SqliteTransaction transaction)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeTransaction, transaction))
                _activeTransaction = null;
        }
    }

    private static DateTimeOffset ParseTimestamp(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    /// <summary>
    /// autocommit 命令执行面 —— 每命令提交到该库单写 worker（经 <see cref="ISqliteWriteHost"/> 按库键控通道，
    /// 禁自建第二套写门）。事务进行中时由 <see cref="SqliteTransaction.Commands"/> 取代本实现。
    /// </summary>
    private sealed class AutocommitExecutor : IDbCommandExecutor
    {
        private readonly ISqliteWriteHost _host;
        private readonly string _connectionString;

        public AutocommitExecutor(ISqliteWriteHost host, string connectionString)
        {
            _host = host;
            _connectionString = connectionString;
        }

        public int ExecuteNonQuery(string sql, IDictionary<string, object>? parameters = null)
            => _host.RunWrite(_connectionString, conn =>
            {
                using var command = CreateCommand(conn, sql, parameters);
                return command.ExecuteNonQuery();
            });

        public long ExecuteInsertReturningId(string sql, IDictionary<string, object>? parameters = null)
            => _host.RunWrite(_connectionString, conn =>
            {
                using var command = CreateCommand(conn, sql, parameters);
                command.ExecuteNonQuery();
                return conn.LastInsertRowId;
            });

        public object? ExecuteScalar(string sql, IDictionary<string, object>? parameters = null)
            => _host.RunWrite(_connectionString, conn =>
            {
                using var command = CreateCommand(conn, sql, parameters);
                var value = command.ExecuteScalar();
                return value is DBNull ? null : value;
            });

        public IReadOnlyList<IReadOnlyDictionary<string, object>> ExecuteQuery(
            string sql, IDictionary<string, object>? parameters = null)
            => _host.RunWrite(_connectionString, conn =>
            {
                var rows = new List<IReadOnlyDictionary<string, object>>();
                using var command = CreateCommand(conn, sql, parameters);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (var index = 0; index < reader.FieldCount; index++)
                        row[reader.GetName(index)] = reader.IsDBNull(index) ? DBNull.Value : reader.GetValue(index);

                    rows.Add(row);
                }

                return (IReadOnlyList<IReadOnlyDictionary<string, object>>)rows;
            });

        private static SQLiteCommand CreateCommand(
            SQLiteConnection connection, string sql, IDictionary<string, object>? parameters)
        {
            var command = new SQLiteCommand(sql, connection);
            if (parameters is not null)
            {
                foreach (var parameter in parameters)
                    command.Parameters.AddWithValue(parameter.Key, parameter.Value);
            }

            return command;
        }
    }

    /// <summary>事务句柄（<see cref="SqliteTransaction"/> 适配）：提交/回滚/未提交即释放 = 回滚；收尾释放进行中事务句柄。</summary>
    private sealed class SqliteUnitOfWork : IInstanceUnitOfWork
    {
        private readonly SqliteInstanceStore _store;
        private readonly SqliteTransaction _transaction;
        private bool _settled;

        public SqliteUnitOfWork(SqliteInstanceStore store, SqliteTransaction transaction)
        {
            _store = store;
            _transaction = transaction;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_settled)
                return;

            _settled = true;
            try
            {
                await _transaction.CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                _store.EndUnitOfWork(_transaction);
            }
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_settled)
                return;

            _settled = true;
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            finally
            {
                _store.EndUnitOfWork(_transaction);
            }
        }

        public async ValueTask DisposeAsync()
        {
            // 未显式提交即释放 = 回滚（事务不隐式提交，I6）。
            if (!_settled)
                await RollbackAsync().ConfigureAwait(false);
        }
    }
}

