using System;
using System.Collections.Generic;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.Local;
using Operation.DataStoreDriven.ReferenceSchema;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UniversalWorkflowFramework.Core.Interfaces;

namespace Operation.DataStoreDriven.Resolvers;

/// <summary>
/// 配置驱动动态解析（组合根）：按 <see cref="PersistenceProvider"/> 从注册表解析具体 <see cref="IPersistenceStore"/>。
/// 切换后端 = 改 <c>Persistence.Provider</c> 配置，零代码改动（IoC 反转验收标准）。
/// </summary>
public static class PersistenceProviderFactory
{
    // Graph/Local 等无原生 checkpoint/快照的后端需要内存占位实现。占位实现收敛至
    // UniversalWorkflowFramework.StorageFramework.Core（InMemoryCheckpointStore/InMemorySnapshotStore），经 IoC 注入（DI 路径自 IServiceProvider 解析；
    // 非 DI 的 Create 路径由调用方显式传入），Persistence 不反向引用 Agent.State。
    private delegate IPersistenceStore StoreFactory(IServiceProvider? sp, IUniversalReferenceManager? manager, PersistenceOptions options,
        ICheckpointStore? placeholderCheckpoints, ISnapshotStore? placeholderSnapshots, IReferenceTypeRegistry? referenceTypeRegistry, IDocumentCodec? codec, ITableSchemaSource? schemaSource);

    private static readonly IReadOnlyDictionary<PersistenceProvider, StoreFactory> _registry =
        new Dictionary<PersistenceProvider, StoreFactory>
        {
            [PersistenceProvider.Sqlite] = (sp, manager, options, pc, ps, registry, codec, schemaSource) => new SqlitePersistenceStore(new SqliteConnectionFactory(options), options, codec, schemaSource),
            [PersistenceProvider.Graph] = (sp, manager, options, pc, ps, registry, codec, schemaSource) => new GraphPersistenceStore(
                pc ?? sp?.GetRequiredService<ICheckpointStore>()
                    ?? throw new NotSupportedException("Graph 后端需要 ICheckpointStore 占位（建议 UniversalWorkflowFramework.StorageFramework.Core.InMemoryCheckpointStore）。"),
                ps ?? sp?.GetRequiredService<ISnapshotStore>()
                    ?? throw new NotSupportedException("Graph 后端需要 ISnapshotStore 占位（建议 UniversalWorkflowFramework.StorageFramework.Core.InMemorySnapshotStore）。")),
            [PersistenceProvider.Local] = (sp, manager, options, pc, ps, registry, codec, schemaSource) => new LocalPersistenceStore(options,
                pc ?? sp?.GetRequiredService<ICheckpointStore>()
                    ?? throw new NotSupportedException("Local 后端需要 ICheckpointStore 占位（建议 UniversalWorkflowFramework.StorageFramework.Core.InMemoryCheckpointStore）。"),
                ps ?? sp?.GetRequiredService<ISnapshotStore>()
                    ?? throw new NotSupportedException("Local 后端需要 ISnapshotStore 占位（建议 UniversalWorkflowFramework.StorageFramework.Core.InMemorySnapshotStore）。")),
        };

    /// <summary>从 IServiceProvider 解析（读取 IOptions&lt;PersistenceOptions&gt;）。供 MS-DI 宿主使用。</summary>
    public static IPersistenceStore Resolve(IServiceProvider sp, PersistenceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(sp);
        var options = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
        // 全表 USharp 化：MS-DI 路径经容器可选解析 codec/schemaSource（组合根注册 UsharpDataPorts.Codec/SchemaSource）；
        // 未注册 ⇒ null ⇒ 回落 JSON/手写 DDL（零行为变更）。
        var codec = sp.GetService<IDocumentCodec>();
        var schemaSource = sp.GetService<ITableSchemaSource>();
        return ResolveCore(sp, options, provider, null, null, null, null, codec, schemaSource);
    }

    /// <summary>从配置对象直接构造（供 Agent 编排器等非 ServiceCollection 组合根使用）。
    /// Graph/Local 后端需经 <paramref name="placeholderCheckpoints"/>/<paramref name="placeholderSnapshots"/> 提供内存占位。
    /// <paramref name="codec"/> 缺省 JSON（零行为变更）；注入 USharp codec ⇒ 快照/文档 USharp 化。</summary>
    public static IPersistenceStore Create(PersistenceOptions options, IUniversalReferenceManager? referenceManager = null,
        ICheckpointStore? placeholderCheckpoints = null, ISnapshotStore? placeholderSnapshots = null, IReferenceTypeRegistry? referenceTypeRegistry = null,
        IDocumentCodec? codec = null, ITableSchemaSource? schemaSource = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ResolveCore(null, options, options.Provider, referenceManager, placeholderCheckpoints, placeholderSnapshots, referenceTypeRegistry, codec, schemaSource);
    }

    private static IPersistenceStore ResolveCore(IServiceProvider? sp, PersistenceOptions options, PersistenceProvider provider,
        IUniversalReferenceManager? referenceManager, ICheckpointStore? placeholderCheckpoints, ISnapshotStore? placeholderSnapshots, IReferenceTypeRegistry? referenceTypeRegistry, IDocumentCodec? codec, ITableSchemaSource? schemaSource)
    {
        if (!_registry.TryGetValue(provider, out var factory))
            throw new NotSupportedException($"未注册的持久化后端: {provider}");
        return factory(sp, referenceManager, options, placeholderCheckpoints, placeholderSnapshots, referenceTypeRegistry, codec, schemaSource);
    }
}
