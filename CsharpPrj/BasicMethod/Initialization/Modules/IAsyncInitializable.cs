namespace BasicMethod.Initialization.Modules;

/// <summary>可异步初始化组件契约（装配期生命周期的最小面）。</summary>
public interface IAsyncInitializable
{
    /// <summary>当前生命周期状态。</summary>
    InitState State { get; }

    /// <summary>初始化（幂等：非 Created 态直接返回）。</summary>
    ValueTask InitializeAsync(CancellationToken ct);
}
