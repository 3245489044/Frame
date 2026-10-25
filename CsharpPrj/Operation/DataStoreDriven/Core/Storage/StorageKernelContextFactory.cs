using System;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>内核上下文工厂：把四散参数收口为 <see cref="StorageKernelContext"/>（U1）。</summary>
public static class StorageKernelContextFactory
{
    public static StorageKernelContext From(
        IDbCommandExecutor executor,
        string tablePrefix,
        IDocumentCodec codec,
        string? directory = null,
        ITableSchemaSource? schemaSource = null,
        IDefaultWorkflowIdProvider? workflowIds = null,
        ITableCatalog? tables = null)
        => new()
        {
            Executor = executor ?? throw new ArgumentNullException(nameof(executor)),
            TablePrefix = tablePrefix ?? string.Empty,
            Codec = codec ?? throw new ArgumentNullException(nameof(codec)),
            Directory = directory ?? string.Empty,
            SchemaSource = schemaSource,
            WorkflowIds = workflowIds ?? ConfiguredDefaultWorkflowIdProvider.Default,
            Tables = tables ?? new TableCatalog(),
        };
}
