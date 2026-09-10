using System;
using Share.HostAssembly.Concurrency;

namespace Share.HostAssembly.Disposal;

/// <summary>
/// 把 <see cref="IAsyncDisposable"/> 适配为 <see cref="IDisposable"/>（同步释放链契约）。
/// 用于把异步释放的资源接入同步释放链；经 <see cref="SyncAsyncBridge"/> 在无同步上下文的线程池上执行，
/// 避免 WinForms/同步上下文环境下 `.GetResult()` 同步等待导致的死锁风险。
/// </summary>
public sealed class AsyncDisposeAdapter : IDisposable
{
    private readonly IAsyncDisposable _target;

    public AsyncDisposeAdapter(IAsyncDisposable target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Dispose() => SyncAsyncBridge.Wait(_target.DisposeAsync);
}
