namespace Modules.Threading;

/// <summary>非阻塞等待扩展：替代命令层/调用方的 <c>Task.Wait()</c>。</summary>
public static class OperationExtensions
{
    /// <summary>有限等待完成信号；超时返回 false（不抛出）。</summary>
    public static async Task<bool> TryWaitAsync(
        this Task task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>有限等待完成信号；超时返回 false（不抛出）。</summary>
    public static async Task<bool> TryWaitAsync<T>(
        this Task<T> task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
