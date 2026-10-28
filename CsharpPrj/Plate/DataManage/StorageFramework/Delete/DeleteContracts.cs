using System;
using System.Collections.Generic;
using System.Linq;
using UniversalWorkflowFramework.StorageFramework.Batch;

namespace UniversalWorkflowFramework.StorageFramework.Delete;

/// <summary>删除模式：<c>Auto</c> = 按 recipe 声明执行（含级联）；<c>Direct</c> = 只删主载体（禁级联）。</summary>
public enum DeleteMode
{
    Auto = 0,
    Cascade = 1,
    Direct = 2,
}

/// <summary>
/// 删除请求（域无关）：域 + 请求键集 + 意图/执行者（供守卫使用）。
/// <para>域键归一由 recipe 负责（<see cref="IDeleteRecipe.ResolveKeys"/>），调用方不做字符串猜域。</para>
/// </summary>
public sealed record DeleteRequest(
    string Domain,
    IReadOnlyList<string> Keys,
    string Intent = "user_requested",
    string Actor = "ui",
    DeleteMode Mode = DeleteMode.Auto)
{
    /// <summary>请求键集（null ⇒ 空集，禁 NRE）。</summary>
    public IReadOnlyList<string> SafeKeys => Keys ?? Array.Empty<string>();
}

/// <summary>
/// 逐项删除结果（结构事实）：请求键 / 实际命中载体键 / 状态 / 探针结论 / 已执行级联 / 可见化原因。
/// <para>状态词表唯一来源 = <see cref="BatchItemStatus"/>；禁自造「视为已删除」等叙述替代状态。</para>
/// </summary>
public sealed record DeleteItemOutcome(
    string RequestedKey,
    string? ResolvedKey,
    BatchItemStatus Status,
    string? Probe,
    IReadOnlyList<string> Cascades,
    string? Message)
{
    /// <summary>是否成功（含幂等跳过）。</summary>
    public bool IsOk => Status is BatchItemStatus.Ok or BatchItemStatus.Skipped;

    /// <summary>按状态建结果（级联清单缺省为空）。</summary>
    public static DeleteItemOutcome Of(
        string key, string? resolvedKey, BatchItemStatus status, string? message,
        string? probe = null, IReadOnlyList<string>? cascades = null)
        => new(key, resolvedKey, status, probe, cascades ?? Array.Empty<string>(), message);

    /// <summary>成功项。</summary>
    public static DeleteItemOutcome Ok(string key, string? resolvedKey, string? probe = null,
        IReadOnlyList<string>? cascades = null, string? message = null)
        => Of(key, resolvedKey, BatchItemStatus.Ok, message, probe, cascades);

    /// <summary>幂等跳过（唯一合法前提：verify 已确认载体不存在）。</summary>
    public static DeleteItemOutcome Skipped(string key, string? probe, string message)
        => Of(key, null, BatchItemStatus.Skipped, message, probe);

    /// <summary>拒绝项（键不可解析 / 守卫拒绝 / 探针-删除不一致 / 级联失败 / verify 未通过）。</summary>
    public static DeleteItemOutcome Rejected(string key, string message, string? resolvedKey = null,
        string? probe = null, IReadOnlyList<string>? cascades = null)
        => Of(key, resolvedKey, BatchItemStatus.Rejected, message, probe, cascades);

    /// <summary>删除载体返回「不存在」时的显式结果。</summary>
    public static DeleteItemOutcome NotFound(string key, string message, string? resolvedKey = null,
        string? probe = null)
        => Of(key, resolvedKey, BatchItemStatus.NotFound, message, probe);
}

/// <summary>整批删除结果（逐项清单；禁整批静默）。</summary>
public sealed record DeleteReceipt(IReadOnlyList<DeleteItemOutcome> Items)
{
    /// <summary>成功项数（含幂等跳过）。</summary>
    public int OkCount => Items.Count(i => i.IsOk);

    /// <summary>失败项数。</summary>
    public int FailCount => Items.Count - OkCount;

    /// <summary>是否全部成功。</summary>
    public bool AllOk => FailCount == 0;

    /// <summary>空结果。</summary>
    public static DeleteReceipt Empty { get; } = new(Array.Empty<DeleteItemOutcome>());
}
