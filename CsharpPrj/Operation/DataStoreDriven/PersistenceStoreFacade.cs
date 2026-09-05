using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline.StaticPipeline;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven;

/// <summary>
/// 持久化门面：组合 Db（Schema 初始化）+ checkpoint + 快照 + 事务工厂，实现 <see cref="IPersistenceStore"/>。
/// 后端无关 —— 各后端（Sqlite/LiteDB/Graph/Local）只需提供组件实例，由本门面统一对外暴露。
/// 资源/引用（IResourceReferenceStore）不再经骨干暴露：引用关系真源为 LiteDB 图库 graph-log.db，
/// 由组合根独立装配（ReferenceGraphCoordinator），骨干不再承载 SQLite ur_resources。
/// </summary>
public class PersistenceStoreFacade : IPersistenceStore
{
    private readonly IDbSchemaInitializer? _schema;
    private readonly Func<CancellationToken, Task<ITransaction>> _transactionFactory;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public PersistenceStoreFacade(
        ICheckpointStore checkpoints,
        IDbSchemaInitializer? schema,
        Func<CancellationToken, Task<ITransaction>> transactionFactory,
        ISnapshotStore snapshots)
    {
        Checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        _schema = schema;
        _transactionFactory = transactionFactory ?? throw new ArgumentNullException(nameof(transactionFactory));
        Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
    }

    public ICheckpointStore Checkpoints { get; }

    public ISnapshotStore Snapshots { get; }

    public virtual async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            // 顺序初始化步骤编排（StaticAsyncPipeline）：Schema 可为 null（CanExecute 跳过），
            // 各步骤 IsFatal=true 保持"任一步失败即中止并抛出"语义；StepExecutionException 回抛原始异常。
            try
            {
                await Pipeline.CreateAsync<PersistenceStoreFacade>()
                    .AddStep("Schema", f => f._schema!.InitializeAsync(ct), canExecute: f => f._schema is not null)
                    .AddStep("Checkpoints", f => f.Checkpoints.InitializeAsync(ct))
                    .AddStep("Snapshots", f => f.Snapshots.InitializeAsync(ct))
                    .Build()
                    .ExecuteOrThrowAsync(this).ConfigureAwait(false);
            }
            catch (StepExecutionException ex)
            {
                PipelineExceptionDispatch.RethrowOriginal(ex);
            }
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
        => _transactionFactory(ct);

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
