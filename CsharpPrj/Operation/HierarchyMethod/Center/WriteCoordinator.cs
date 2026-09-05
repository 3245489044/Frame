using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Threading;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// 单一确定性写门（R3 canonical）：把 ur_nodes / KeyMapping 分类写 / 引用图权威边的写统一入口。
/// 本类为 <c>Modules.Threading.SerialWriteGate</c> 的**薄门面**：SemaphoreSlim 串行单写 + 有界等待（BoundedExecution）；
/// 锁被持卡死（预算内未取得写门）→ 抛 <see cref="StoreTimeoutException"/> 自证写对象（不再无日志卡死）。
/// 可选：构造时经 <see cref="SingleWriteGuard"/> 注册为某写目标的唯一确定性写路径（运行时 AssertSingleWriter 双保险）。
/// </summary>
public sealed class WriteCoordinator : IDisposable
{
    private readonly SerialWriteGate _inner;

    public WriteCoordinator(TimeSpan? defaultTimeout = null,
        string? singleWriterName = null, string? singleWriteTarget = null)
    {
        _inner = new SerialWriteGate(defaultTimeout, singleWriterName, singleWriteTarget);
    }

    /// <summary>串行单写 + 有界等待执行一次写入。默认预算见构造 <paramref name="defaultTimeout"/>；显式 <paramref name="timeout"/> 覆盖。</summary>
    public async Task<T> RunWriteAsync<T>(
        string target, Func<CancellationToken, Task<T>> write,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            return await _inner.RunWriteAsync(target, write, timeout, ct).ConfigureAwait(false);
        }
        catch (BoundedTimeoutException ex)
        {
            throw new StoreTimeoutException(ex.Subject, ex.Budget); // 框架超时 → Operation 强类型（IBudgetExhausted）保序
        }
    }

    /// <summary>串行单写 + 有界等待执行一次写入（无返回值体）。</summary>
    public async Task RunWriteAsync(
        string target, Func<CancellationToken, Task> write,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            await _inner.RunWriteAsync(target, write, timeout, ct).ConfigureAwait(false);
        }
        catch (BoundedTimeoutException ex)
        {
            throw new StoreTimeoutException(ex.Subject, ex.Budget);
        }
    }

    public void Dispose() => _inner.Dispose();
}
