namespace Modules.Threading;

/// <summary>异步操作的可选配置。</summary>
public sealed class AsyncOperationOptions
{
    /// <summary>空闲看门狗窗口；null=用调度器默认；&lt;=0=不启用空闲看门狗。</summary>
    public TimeSpan? IdleTimeout { get; init; }

    /// <summary>绝对时限；null=不设绝对上限（默认）。</summary>
    public TimeSpan? AbsoluteTimeout { get; init; }

    /// <summary>防重入去重键；同一键并发重复执行时，后到者直接返回 Failed(IsReentering)。</summary>
    public object? ReentryKey { get; init; }

    /// <summary>隔离域（如每次调用独立 data-dir）。</summary>
    public string? IsolationKey { get; init; }

    /// <summary>可杀对象（如 node / ClineCore 进程）。</summary>
    public IProcessHandle? Process { get; init; }

    /// <summary>外部取消令牌（可选）：与内部 Cts 链接，外部（如命令层等待超时）可提前终止本操作。</summary>
    public CancellationToken ExternalToken { get; init; }
}
