using System;
using System.Text.Json;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Share.TaskStatusPool.TaskPool;

namespace Operation.DataStoreDriven.Resolvers;

/// <summary>
/// 任务池持久化存储解析（组合根）：按后端 Provider 解析具体 <see cref="ITaskPoolPersistence{TItem}"/>。
/// <para>控制反转后本类位于 <c>Domains</c> 程序集；主入口 = <see cref="Create{TItem}(StorageKernelContext)"/>。</para>
/// </summary>
public static class TaskPoolStoreFactory
{
    /// <summary>Sqlite 任务池存储（持久化 ur_tasks）：内核上下文单参入口。</summary>
    public static ITaskPoolPersistence<TItem> Create<TItem>(StorageKernelContext kernel)
        where TItem : ITaskPoolItem
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return new SqliteTaskPoolStore<TItem>(kernel.Executor, kernel.TablePrefix, jsonOptions: null, codec: kernel.Codec);
    }

    /// <summary>Sqlite 任务池存储（旧装配签名；D-5 后删除）。</summary>
    [Obsolete("使用 Create<TItem>(StorageKernelContext)（控制反转后的单参入口）。")]
    public static ITaskPoolPersistence<TItem> Create<TItem>(IDbCommandExecutor executor, string tablePrefix, JsonSerializerOptions? jsonOptions = null, IDocumentCodec? codec = null)
        where TItem : ITaskPoolItem
        => new SqliteTaskPoolStore<TItem>(executor, tablePrefix, jsonOptions, codec);

    /// <summary>内存任务池存储占位（非 Sqlite 后端 / 测试）。</summary>
    public static ITaskPoolPersistence<TItem> CreateInMemory<TItem>()
        where TItem : ITaskPoolItem
        => new InMemoryTaskPoolPersistence<TItem>();
}

