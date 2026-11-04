using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>
/// 引用存储能力声明（结构事实）：是否支持读取入边（<c>incoming</c>）。
/// <para>不具备该能力时，守卫的「父计数 / 单父」类谓词**不可判定**（禁以 0 冒充「无父」，
/// 权威 <c>AX.NoSilentOutlet</c>）；实现须显式声明，禁靠调用方猜测。</para>
/// </summary>
public interface IReferenceIncomingCapability
{
    /// <summary>是否支持 <c>GetReferencesAsync(..., includeIncoming: true)</c> 的真实入边结果。</summary>
    bool SupportsIncoming { get; }
}

/// <summary>
/// 批量读能力声明（结构事实）：一次锁内读取多个端点的邻接，替代 N+1 单点读。
/// <para>不具备该能力 ⇒ 批量派生退化为逐点读（语义不变，仅成本上升；禁伪造批量语义）。</para>
/// </summary>
public interface IReferenceBatchReader
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceReference>>> GetReferencesManyAsync(
        IReadOnlyCollection<ResourceIdentifier> resourceIds,
        bool includeIncoming = false,
        CancellationToken ct = default);
}