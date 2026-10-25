using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 通用存储内核上下文：域模块唯一可见的介质句柄，不含任何领域词汇。
/// <para>把四处散落的 <c>(executor, tablePrefix, codec, directory)</c> 收为一个不可变上下文（U1）。</para>
/// </summary>
public sealed class StorageKernelContext
{
    /// <summary>命令执行器（环境事务感知由存储基类包装）。</summary>
    public required IDbCommandExecutor Executor { get; init; }

    /// <summary>统一表前缀。</summary>
    public string TablePrefix { get; init; } = string.Empty;

    /// <summary>文档编解码（USharp codec / JSON codec）。</summary>
    public required IDocumentCodec Codec { get; init; }

    /// <summary>存储目录（图库/实体库同目录判据）。</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>表清单（核心表 + 各域 <c>DeclareTables</c> 汇入）。</summary>
    public ITableCatalog Tables { get; init; } = new TableCatalog();

    /// <summary>表结构声明源（USharp <c>[Table]</c> 投影；缺件回落手写 DDL）。</summary>
    public ITableSchemaSource? SchemaSource { get; init; }

    /// <summary>默认工作流 Id 提供者（U9；缺省保持既有行为）。</summary>
    public IDefaultWorkflowIdProvider WorkflowIds { get; init; } = ConfiguredDefaultWorkflowIdProvider.Default;
}
