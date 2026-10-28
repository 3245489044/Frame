using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Batch;

namespace UniversalWorkflowFramework.StorageFramework.Delete;

/// <summary>
/// 载体探针结论：是否存在 + 实际命中键 + 判据来源（结构事实）。
/// <para>探针是 <c>Ok</c> 与 <c>Skipped</c> 的唯一分界：未命中探针不得直接报成功。</para>
/// </summary>
public sealed record DeleteProbe(bool Exists, string? CarrierKey, string Evidence)
{
    /// <summary>命中。</summary>
    public static DeleteProbe Found(string carrierKey, string evidence)
        => new(true, carrierKey, evidence);

    /// <summary>未命中（<paramref name="evidence"/> 记录已探测的键形态/来源，禁空）。</summary>
    public static DeleteProbe Absent(string evidence) => new(false, null, evidence);
}

/// <summary>
/// 级联步骤（声明式）：<paramref name="Requires"/> 表达依赖（DAG），顺序由 fabric 拓扑求解；
/// 禁以「先…再…」文本表达流程（权威: axioms:SEP.DeclarativeProcessDisjoint）。
/// </summary>
public sealed record DeleteCascade(
    string Name,
    IReadOnlyList<string> Requires,
    Func<string, DeleteRequest, CancellationToken, Task<BatchItemOutcome>> Execute);

/// <summary>
/// 域删除配方（域只交数据，不交流程）：
/// 键文法归一 → 载体探针 → 主载体删除 →（可选）级联 → 守卫。
/// <para>实现不得在内部再走第二套删除入口（禁 AgentWriteSources / 节点域端口回环）。</para>
/// </summary>
public interface IDeleteRecipe
{
    /// <summary>域键（与 <c>IStoreCatalog</c>/<c>AgentStoreCatalog</c> 同一词表）。</summary>
    string Domain { get; }

    /// <summary>请求键 → 候选载体键集（顺序敏感：规范形态优先，兼容形态兜底；空集 ⇒ 键不可解析）。</summary>
    IReadOnlyList<string> ResolveKeys(string requestedKey);

    /// <summary>是否要求探针（false ⇒ 载体自身即判据，由 DeleteAsync 的 Ok/NotFound 表达）。</summary>
    bool ProbeRequired => true;

    /// <summary>载体探针（仅 <see cref="ProbeRequired"/>=true 时调用）。</summary>
    Task<DeleteProbe> ProbeAsync(string carrierKey, CancellationToken ct = default);

    /// <summary>删除主载体（幂等由实现负责；未命中返回 <see cref="BatchItemStatus.NotFound"/>）。</summary>
    Task<BatchItemOutcome> DeleteAsync(string carrierKey, DeleteRequest request, CancellationToken ct = default);

    /// <summary>守卫（在途/受保护/未装配）；返回非空结构码 ⇒ 拒绝。</summary>
    Task<string?> GuardAsync(string carrierKey, DeleteRequest request, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    /// <summary>声明式级联（依赖 DAG，非步骤序）。</summary>
    IReadOnlyList<DeleteCascade> Cascades => Array.Empty<DeleteCascade>();
}
