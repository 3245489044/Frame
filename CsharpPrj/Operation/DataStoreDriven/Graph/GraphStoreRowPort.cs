using System;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Batch;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// <see cref="IStoreRowPort"/> 的 **LiteDB 引用图实现**：键 = 资源 id，文档 = 资源 JSON；
/// 删除 = 删边 + 资源（图的唯一 owner = 引用图协调器，经委托注入，本类不直接持图引擎）。
/// <para>
/// 语义差异（对 D2 的落实）：图不由「键 + 文档 upsert」表达，故写 / 删一律经注入的**图原语委托**；
/// 未装配（守卫缺位）⇒ <see cref="BatchItemStatus.Skipped"/> 可见化。
/// </para>
/// </summary>
public sealed class GraphStoreRowPort : IStoreRowPort
{
    private readonly Func<string, string?> _read;
    private readonly Func<string, string, BatchItemOutcome> _write;
    private readonly Func<string, BatchItemOutcome> _delete;

    /// <param name="read">资源读取（key → 文档 JSON；不存在 ⇒ null）。</param>
    /// <param name="write">资源写入（key + 文档 JSON → 逐项结果）。</param>
    /// <param name="delete">资源删除（key → 逐项结果；含删边）。</param>
    /// <param name="place">物理落点（缺省 <c>graph</c>）。</param>
    /// <param name="keyColumn">键列名（缺省 <c>resourceId</c>）。</param>
    public GraphStoreRowPort(
        Func<string, string?> read,
        Func<string, string, BatchItemOutcome> write,
        Func<string, BatchItemOutcome> delete,
        string place = "graph",
        string keyColumn = "resourceId")
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _delete = delete ?? throw new ArgumentNullException(nameof(delete));
        Place = place;
        KeyColumn = keyColumn;
    }

    /// <inheritdoc />
    public string Place { get; }

    /// <inheritdoc />
    public string KeyColumn { get; }

    /// <inheritdoc />
    public bool TryRead(string key, out string docText)
    {
        docText = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var doc = _read(key);
        if (string.IsNullOrEmpty(doc)) return false;
        docText = doc!;
        return true;
    }

    /// <inheritdoc />
    public BatchItemOutcome Upsert(string key, string docText)
        => string.IsNullOrWhiteSpace(key)
            ? BatchItemOutcome.Fail(key ?? string.Empty, BatchItemStatus.Skipped, "图域未开放写入")
            : _write(key, docText ?? string.Empty);

    /// <inheritdoc />
    public BatchItemOutcome Delete(string key)
        => string.IsNullOrWhiteSpace(key)
            ? BatchItemOutcome.Fail(key ?? string.Empty, BatchItemStatus.Rejected, "空键")
            : _delete(key);
}
