using System;
using System.Text.Json;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Share.TaskStatusPool.TaskPool;

namespace Operation.DataStoreDriven.Resolvers;

/// <summary>
/// 任务池持久化存储解析（组合根）：按后端 Provider 解析具体 <see cref="ITaskPoolPersistence{TItem}"/>。
/// Sqlite → <see cref="SqliteTaskPoolStore{TItem}"/>（ur_tasks 持久化）；其余后端 → 内存占位
/// <see cref="InMemoryTaskPoolPersistence{TItem}"/>（重启即失，仅接线/原型，对齐三区内存占位语义）。
/// 与 <see cref="DataStoreProviderFactory"/> 解耦：任务池是独立状态机存储，不复用 IPersistenceStore 骨干契约。
/// </summary>
public static class TaskPoolStoreFactory
{
    /// <summary>Sqlite 任务池存储（持久化 ur_tasks）。</summary>
    public static ITaskPoolPersistence<TItem> Create<TItem>(IDbCommandExecutor executor, string tablePrefix, JsonSerializerOptions? jsonOptions = null)
        where TItem : ITaskPoolItem
        => new SqliteTaskPoolStore<TItem>(executor, tablePrefix, jsonOptions);

    /// <summary>内存任务池存储占位（非 Sqlite 后端 / 测试）。</summary>
    public static ITaskPoolPersistence<TItem> CreateInMemory<TItem>()
        where TItem : ITaskPoolItem
        => new InMemoryTaskPoolPersistence<TItem>();
}
