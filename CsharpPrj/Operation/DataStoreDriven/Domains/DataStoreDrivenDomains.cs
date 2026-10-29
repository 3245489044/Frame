using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Sqlite;
using Share.Conversation;
using Share.Conversation.Aggregates;
using Share.Conversation.Ports;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Domains;

/// <summary>
/// 域清单单源：新增域只改本文件（<see cref="All"/>）。
/// <para>顺序冻结：History → Session → TaskPool（表声明顺序稳定；双读回退通道已删除，无跨模块读依赖）。</para>
/// </summary>
public static class DataStoreDrivenDomains
{
    /// <summary>全部域模块（唯一清单）。</summary>
    public static IReadOnlyList<IDomainStoreModule> All(
        StorageKernelContext kernel,
        ILegacyContainerClassifier? legacyHistoryClassifier = null)
        => new IDomainStoreModule[]
        {
            new ConversationLogDomainModule(kernel, legacyHistoryClassifier),
            new SessionDomainModule(kernel),
            new TaskPoolDomainModule(kernel),
        };
}

/// <summary>历史域模块：会话历史容器表 + IConversationLogStore / IConversationLogRootStore 注册。</summary>
public sealed class ConversationLogDomainModule : IDomainStoreModule
{
    private readonly StorageKernelContext _kernel;
    private readonly ILegacyContainerClassifier? _legacy;

    public ConversationLogDomainModule(StorageKernelContext kernel, ILegacyContainerClassifier? legacy = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _legacy = legacy;
    }

    public string Name => "history";

    public void DeclareTables(ITableCatalog catalog)
    {
        var table = PersistenceHelper.ResolveTablePrefix(_kernel.TablePrefix) + TableNames.Histories;
        catalog.Add(new DelegateTableDeclaration(table, (executor, ct) =>
            new SqliteConversationLogStore(executor, _kernel.TablePrefix, _kernel.Codec, _kernel.SchemaSource).InitializeAsync(ct)));
    }

    public void Register(IDomainStoreRegistry registry, StorageKernelContext kernel)
    {
        registry.Register(typeof(IConversationLogStore), () =>
            new SqliteConversationLogStore(kernel.Executor, kernel.TablePrefix, kernel.Codec, kernel.SchemaSource));
        registry.Register(typeof(IConversationLogRootStore), () =>
            new SqliteContainerRootStore(kernel.Executor, kernel.TablePrefix, kernel.Codec, kernel.SchemaSource, _legacy));
    }
}

/// <summary>会话域模块：ur_sessions / ur_session_logs 表 + ISessionStore / ISessionTranscriptStore 注册。</summary>
public sealed class SessionDomainModule : IDomainStoreModule
{
    private readonly StorageKernelContext _kernel;

    public SessionDomainModule(StorageKernelContext kernel)
        => _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));

    public string Name => "session";

    public void DeclareTables(ITableCatalog catalog)
    {
        var prefix = PersistenceHelper.ResolveTablePrefix(_kernel.TablePrefix);
        catalog.Add(new DelegateTableDeclaration(prefix + TableNames.Sessions, (executor, ct) =>
            new SqliteSessionStore(executor, _kernel.TablePrefix, _kernel.Codec, _kernel.SchemaSource).InitializeAsync(ct)));
        catalog.Add(new DelegateTableDeclaration(prefix + TableNames.SessionLogs, (executor, ct) =>
            new SqliteSessionTranscriptStore(executor, _kernel.TablePrefix, _kernel.Codec, _kernel.SchemaSource).InitializeAsync(ct)));
    }

    public void Register(IDomainStoreRegistry registry, StorageKernelContext kernel)
    {
        registry.Register(typeof(ISessionStore), () =>
            new SqliteSessionStore(kernel.Executor, kernel.TablePrefix, kernel.Codec, kernel.SchemaSource));
        registry.Register(typeof(ISessionTranscriptStore), () =>
            new SqliteSessionTranscriptStore(kernel.Executor, kernel.TablePrefix, kernel.Codec, kernel.SchemaSource));
    }
}

/// <summary>任务池域模块：ur_tasks 表（复用核心 <see cref="SqliteSchema"/> 执行器）。</summary>
public sealed class TaskPoolDomainModule : IDomainStoreModule
{
    private readonly StorageKernelContext _kernel;

    public TaskPoolDomainModule(StorageKernelContext kernel)
        => _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));

    public string Name => "taskpool";

    public void DeclareTables(ITableCatalog catalog)
    {
        var table = PersistenceHelper.ResolveTablePrefix(_kernel.TablePrefix) + TableNames.Tasks;
        catalog.Add(new DelegateTableDeclaration(table, (executor, ct) =>
            new SqliteSchema(executor, _kernel.TablePrefix, _kernel.SchemaSource).InitializeAsync(ct)));
    }

    public void Register(IDomainStoreRegistry registry, StorageKernelContext kernel)
    {
        // 任务池为开放泛型（ITaskPoolPersistence<TItem>），由宿主经 TaskPoolStoreFactory.Create<TItem>(kernel) 解析。
        _ = registry;
        _ = kernel;
    }
}
