namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 同步三段协议（clinerules AX-CORE.SyncProtocolTriple）：Commit→Compensate→Observe。
/// Committed(O) 后值冻结（消费方：TH-3 确定性、TH-1 跨域同步）。
/// </summary>
public static class SyncProtocol
{
    /// <summary>Commit→Observe；Commit 抛异常时先 Compensate 再重抛。</summary>
    public static async Task<T> CommitCompensateObserveAsync<T>(
        Func<Task<T>> commit,
        Func<Exception, Task> compensate,
        Func<T, Task> observe)
    {
        commit = PortGuard.Required(commit, nameof(commit));
        try
        {
            var value = await commit().ConfigureAwait(false);
            if (observe != null) await observe(value).ConfigureAwait(false);
            return value; // Committed(O)：值冻结
        }
        catch (Exception ex)
        {
            if (compensate != null) await compensate(ex).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>同步便捷重载。</summary>
    public static T CommitCompensateObserve<T>(
        Func<T> commit,
        Action<Exception> compensate,
        Action<T> observe)
    {
        try
        {
            var value = commit();
            observe?.Invoke(value);
            return value;
        }
        catch (Exception ex)
        {
            compensate?.Invoke(ex);
            throw;
        }
    }
}
