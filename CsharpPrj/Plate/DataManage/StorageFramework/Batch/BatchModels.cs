using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalWorkflowFramework.StorageFramework.Batch;

/// <summary>
/// 批处理逐项状态（存储层唯一字面量源）。域层/UI 只认本枚举，禁各自造词。
/// </summary>
public enum BatchItemStatus
{
    /// <summary>已落库 / 已删除。</summary>
    Ok,

    /// <summary>幂等跳过（无实际变更）。</summary>
    Skipped,

    /// <summary>目标不存在。</summary>
    NotFound,

    /// <summary>被拒（键非法 / 约束冲突 / 执行异常）。</summary>
    Rejected,

    /// <summary>冲突（并发写 / 版本不符）。</summary>
    Conflict,
}

/// <summary>批处理逐项结果（单一来源；域层/UI 直接消费，禁整批静默）。</summary>
/// <param name="Key">项键（行键 / 边 id）。</param>
/// <param name="Status">逐项状态。</param>
/// <param name="Message">可空可见化原因（结构事实；禁叙述性理由）。</param>
public sealed record BatchItemOutcome(string Key, BatchItemStatus Status, string? Message)
{
    /// <summary>该项是否成功（含幂等跳过）。</summary>
    public bool IsOk => Status is BatchItemStatus.Ok or BatchItemStatus.Skipped;

    /// <summary>按状态建成功项。</summary>
    public static BatchItemOutcome Ok(string key) => new(key, BatchItemStatus.Ok, null);

    /// <summary>按状态建失败项。</summary>
    public static BatchItemOutcome Fail(string key, BatchItemStatus status, string? message)
        => new(key, status, message);
}

/// <summary>批处理汇总结果（逐项清单 + 计数；禁整批静默）。</summary>
/// <param name="Items">逐项结果（与请求同序）。</param>
public sealed record BatchResult(IReadOnlyList<BatchItemOutcome> Items)
{
    /// <summary>成功项数（含幂等跳过）。</summary>
    public int OkCount => Items.Count(i => i.IsOk);

    /// <summary>失败项数。</summary>
    public int FailCount => Items.Count - OkCount;

    /// <summary>是否全部成功。</summary>
    public bool AllOk => FailCount == 0;

    /// <summary>空结果。</summary>
    public static BatchResult Empty { get; } = new(Array.Empty<BatchItemOutcome>());
}

/// <summary>批读结果（行集 + 可空可见化原因）。</summary>
/// <param name="Rows">行集（列名 → 值）。</param>
/// <param name="Message">可空可见化原因（表不存在 / 键非法 / 执行失败）。</param>
public sealed record BatchReadResult(
    IReadOnlyList<IReadOnlyDictionary<string, object>> Rows,
    string? Message = null)
{
    /// <summary>行数。</summary>
    public int Count => Rows.Count;
}

/// <summary>批读请求：表 + 键集或选择 + 行数封顶（沿用 <see cref="Sqlite.SqliteTableReader"/> 语义）。</summary>
/// <param name="Table">表名（须在白名单内，防注入）。</param>
/// <param name="Keys">键集（null/空 = 整表前 N 行）。</param>
/// <param name="KeyColumn">键列名（缺省 <c>NodeKey</c>）。</param>
/// <param name="Limit">行数封顶（缺省 200，最高 1000）。</param>
public sealed record BatchReadRequest(
    string Table,
    IReadOnlyList<string>? Keys = null,
    string KeyColumn = "NodeKey",
    int Limit = 200);

/// <summary>批写行（携带原始文档文本；记录编解码由域层负责）。</summary>
/// <param name="Key">行键（唯一键列值）。</param>
/// <param name="Doc">文档文本（真源；如 USharp <c>[Record]</c> 文本）。</param>
public sealed record BatchWriteRow(string Key, string Doc);

/// <summary>批写请求：表 + 行集（行级 upsert）。</summary>
/// <param name="Table">表名。</param>
/// <param name="Rows">行集。</param>
/// <param name="KeyColumn">唯一键列名（缺省 <c>NodeKey</c>）。</param>
/// <param name="DocColumn">文档列名（缺省 <c>Doc</c>）。</param>
public sealed record BatchWriteRequest(
    string Table,
    IReadOnlyList<BatchWriteRow> Rows,
    string KeyColumn = "NodeKey",
    string DocColumn = "Doc");

/// <summary>批删请求：表 + 键集（逐键结果）。</summary>
/// <param name="Table">表名。</param>
/// <param name="Keys">键集。</param>
/// <param name="KeyColumn">键列名（缺省 <c>NodeKey</c>）。</param>
public sealed record BatchDeleteRequest(
    string Table,
    IReadOnlyList<string> Keys,
    string KeyColumn = "NodeKey");

/// <summary>
/// **通用批量操作端口**（引擎级、零领域）：把「表/容器 + 键集/选择 + 行操作」统一成批。
/// <para>
/// 纪律：批写绑**库级单写通道 / worker 事务**（原子 + 单写者）；批读沿用
/// <see cref="Sqlite.SqliteTableReader"/> 语义（白名单 + LIMIT 封顶 + 只读）；逐项
/// <see cref="BatchItemOutcome"/>（禁整批静默）。域记录形态（USharp 编解码 / 引用图 / 级联）
/// **留在域层**，本端口只提供「行级批读/批写/批删 + 逐项结果」。
/// </para>
/// </summary>
public interface IBatchStore
{
    /// <summary>批读：键集或选择 → 行集。</summary>
    BatchReadResult Read(BatchReadRequest request);

    /// <summary>批写：行级 upsert（携带原始文档文本）。</summary>
    BatchResult Upsert(BatchWriteRequest request);

    /// <summary>批删：键集 → 逐键结果。</summary>
    BatchResult Delete(BatchDeleteRequest request);
}
