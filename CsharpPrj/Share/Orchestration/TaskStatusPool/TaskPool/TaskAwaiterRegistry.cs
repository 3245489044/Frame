using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务完成等待者注册表（通用，Agent 无关）：把「入队并等待任务产物」的 TCS 生命周期
/// （注册 → 兑现 → 失败 → 移除）收敛为单一实现，供 <see cref="TaskPoolHost{TItem,TResult}"/>
/// 与各宿主适配器共用，禁止各自维护 <c>Dictionary&lt;taskKey, TaskCompletionSource&gt;</c>。
/// <para>纪律：同一 taskKey 只允许一个在途等待者（冲突显式返回 false，由调用方抛明确异常）；
/// 取消/入队失败必须 <see cref="Remove"/>，否则任务键会永久占用等待位。</para>
/// </summary>
public sealed class TaskAwaiterRegistry<TResult>
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TResult>> _awaiters =
        new(StringComparer.Ordinal);

    /// <summary>当前在途等待者数量（诊断：不随时间无界增长）。</summary>
    public int Count => _awaiters.Count;

    /// <summary>注册一个等待位（同键已存在 ⇒ false，不覆盖先到者）。</summary>
    public bool TryRegister(string taskKey, out Task<TResult>? task)
    {
        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_awaiters.TryAdd(taskKey, tcs))
        {
            task = null;
            return false;
        }

        task = tcs.Task;
        return true;
    }

    /// <summary>兑现等待方（不存在 ⇒ no-op；重试后已移除则不二次兑现）。</summary>
    public void Complete(string taskKey, TResult result)
    {
        if (_awaiters.TryRemove(taskKey, out var tcs)) tcs.TrySetResult(result);
    }

    /// <summary>以异常兑现等待方（不存在 ⇒ no-op）。</summary>
    public void Fail(string taskKey, Exception error)
    {
        if (_awaiters.TryRemove(taskKey, out var tcs)) tcs.TrySetException(error);
    }

    /// <summary>移除等待位（取消 / 入队失败 / 超时后调用，保证任务键可再次等待）。</summary>
    public void Remove(string taskKey) => _awaiters.TryRemove(taskKey, out _);

    /// <summary>是否已有在途等待者。</summary>
    public bool IsWaiting(string taskKey) => _awaiters.ContainsKey(taskKey);
}
