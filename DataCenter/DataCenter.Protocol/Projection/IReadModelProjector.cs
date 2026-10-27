#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataCenter.Protocol.Projection;

/// <summary>
/// 一次读的输入上下文：声明 + 参数 + 归属 + 本拍版本。
/// </summary>
/// <param name="Spec">读模型声明。</param>
/// <param name="Args">读参数（名字 → 值；缺省空表）。</param>
/// <param name="Owner">归属（缓存按 owner 隔离；必填）。</param>
/// <param name="Version">读前版本（用于投影侧可见化，不用于自判失效）。</param>
public sealed record ReadModelContext(
    ReadModelSpec Spec,
    IReadOnlyDictionary<string, string?> Args,
    object Owner,
    long Version);

/// <summary>
/// 读模型投影器：<b>只做「源 → DTO」</b>，不取缓存、不看版本、不写库（单一职责；缓存/版本/隔离由宿主负责）。
/// </summary>
public interface IReadModelProjector
{
    /// <summary>投影（实现方不得返回 <c>null</c> 值充当 Ok）。</summary>
    Task<ReadOutcome<object>> ProjectAsync(ReadModelContext context, CancellationToken cancellationToken);
}
