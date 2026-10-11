using System;

namespace Modules.Threading;

/// <summary>
/// **同步进度接收器**：<c>Report</c> 在**调用线程内联**执行（取代 <see cref="Progress{T}"/>）。
/// <para>
/// 存在理由：<see cref="Progress{T}"/> 语义为「有同步上下文则 Post、否则线程池投递」——
/// 在无 UI 上下文的后台线程上 ⇒ 回调序与到达序不一致（过程节点 <c>seq</c> 语义 = 到达序，即失真），
/// 且可能迟到到轮末结算之后 ⇒ 容器已释放，事件被丢弃（只剩缺口日志）。
/// 故凡「事件顺序即语义」的进度回调（过程节点发射、工具观测等）一律使用本类型。
/// </para>
/// </summary>
/// <typeparam name="T">进度载荷类型。</typeparam>
public sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;

    /// <param name="onReport">内联回调（不得为空）。</param>
    public SyncProgress(Action<T> onReport) => _onReport = onReport ?? throw new ArgumentNullException(nameof(onReport));

    /// <inheritdoc />
    public void Report(T value) => _onReport(value);
}
