using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// Sqlite 持久化门面：装配 SqliteConnectionFactory + SqliteSchema + SqliteTransaction，实现 <see cref="IPersistenceStore"/>。
/// 资源/引用不在此承载（真源为 LiteDB 图库）；checkpoint 经节点库承载（<see cref="NodeCheckpointStore"/>，由数据驱动层注入），
/// 骨干此槽位仅保留内存占位（<see cref="InMemoryCheckpointStore"/>）满足门面契约，不再使用独立 ur_checkpoints 表。
/// </summary>
public sealed class SqlitePersistenceStore : PersistenceStoreFacade
{
    private readonly SqliteConnectionFactory _factory;
    private readonly PersistenceOptions _options;

    public SqlitePersistenceStore(IDbConnectionFactory connectionFactory, PersistenceOptions options, IDocumentCodec? codec = null, ITableSchemaSource? schemaSource = null)
        : base(new InMemoryCheckpointStore(),
               BuildSchema(connectionFactory, options, schemaSource),
               ct => Task.FromResult<ITransaction>(new SqliteTransaction(
                   ThreadingSqliteWriteHost.Shared, connectionFactory.ConnectionString)),
               BuildSnapshots(connectionFactory, options, codec))
    {
        _factory = connectionFactory as SqliteConnectionFactory
            ?? throw new ArgumentException("SqlitePersistenceStore 需要 SqliteConnectionFactory。", nameof(connectionFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>面向组合根便捷构造：直接由配置 + 基路径创建（codec 缺省 JSON）。</summary>
    public static IPersistenceStore Create(PersistenceOptions options, string? basePath = null, IDocumentCodec? codec = null, ITableSchemaSource? schemaSource = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var factory = new SqliteConnectionFactory(options, basePath);
        return new SqlitePersistenceStore(factory, options, codec, schemaSource);
    }

    public override async Task InitializeAsync(CancellationToken ct = default)
    {
        _factory.EnsureFileCreated();
        if (_options.AutoCreateSchema)
            await base.InitializeAsync(ct).ConfigureAwait(false);
        else
        {
            // 不自动建表时仍初始化子存储（连接预热），跳过 Schema 初始化。
            await Checkpoints.InitializeAsync(ct).ConfigureAwait(false);
            await Snapshots.InitializeAsync(ct).ConfigureAwait(false);
        }
    }

    private static IDbSchemaInitializer BuildSchema(IDbConnectionFactory factory, PersistenceOptions options, ITableSchemaSource? schemaSource)
        => new SqliteSchema(new SqliteSession(factory), Table(options), schemaSource);

    private static ISnapshotStore BuildSnapshots(IDbConnectionFactory factory, PersistenceOptions options, IDocumentCodec? codec)
        => new SqliteSnapshotStore(new SqliteSession(factory), Table(options) + TableNames.Tasks, codec);

    private static string Table(PersistenceOptions options)
        => PersistenceHelper.ResolveTablePrefix(options.TablePrefix);
}
