using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 批量成员读委托（UMF · R3）：输入 = **容器键集**（source 侧），输出 = 容器键 → 成员键集（target 侧）。
/// <para>为什么按 MountId 注册而不是挂 <see cref="IEntityDomain"/>：同一实体可同时是多个关系的容器
///（task 既是 <c>node_to_task</c> 也是 <c>task_tree</c> 的 Source），按实体域无法消歧；
/// 且批量语义天然是「关系作用域」而非「实体域」属性。</para>
/// </summary>
/// <param name="containerKeys">容器键集（已被 <see cref="MountScope.ContainerKey"/> 给出）。</param>
/// <param name="ns">命名空间轴（可为空 = 由声明面单源解析）。</param>
/// <param name="ct">取消令牌。</param>
public delegate Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> MountBatchReader(
    IReadOnlyList<string> containerKeys, string? ns, CancellationToken ct);

/// <summary>
/// 批量读注册表（UMF · R3，唯一注册点）：MountId → 一次域委托。
/// <para>通用性：<c>UniversalReference</c> 不认识 task/node/session，批量实现由组合根以委托注入；
/// 新增实体域 = 注册一行，门面与读路径零改动。</para>
/// </summary>
public sealed class MountBatchRegistry
{
    private readonly Dictionary<string, MountBatchReader> _readers = new(StringComparer.Ordinal);

    /// <summary>注册批量委托（重复 ⇒ 抛：禁静默覆盖）。</summary>
    public void Register(string mountId, MountBatchReader reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountId);
        ArgumentNullException.ThrowIfNull(reader);
        if (!_readers.TryAdd(mountId, reader))
            throw new InvalidOperationException($"批量读重复注册：{mountId}（单一实现原则）");
    }

    /// <summary>取批量委托（未注册 ⇒ null，调用方须可见化回退）。</summary>
    public MountBatchReader? TryGet(string mountId)
        => !string.IsNullOrWhiteSpace(mountId) && _readers.TryGetValue(mountId, out var reader) ? reader : null;

    /// <summary>已注册批量委托数（结构计数，门禁判据）。</summary>
    public int Count => _readers.Count;
}
