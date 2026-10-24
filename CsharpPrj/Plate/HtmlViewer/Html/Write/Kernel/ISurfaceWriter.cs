#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 批量写入口（L1 唯一写面）：一拍一个 batch = 一次调度（一次跨进程批量脚本）。
/// <para>单写者：实现内仍按控件串行；预算与车道在此裁决，不产生第二条写落地。</para>
/// </summary>
public interface ISurfaceWriter
{
    /// <summary>提交一批节计划；返回结果代数（未变/失败/部分/全部，单一裁决点 = <see cref="WriteOutcome.Classify"/>）。</summary>
    ValueTask<UiWriteResult> SubmitAsync(
        IReadOnlyList<SectionPlan> batch, UiWriteSource source, string? traceId, CancellationToken ct = default);
}
