namespace Share.StateManagement
{
    // Failed: 渲染失败终态（保留 ConfigVersion/LastError）。重试必须走完整事务，不得被幂等短路吞掉（E1 修复）。
    public enum ScopeState { Empty, Declared, Loading, Loaded, Failed, Destroying, Destroyed }

    public sealed record ScopeLifecycleInfo(
        string Scope,
        ScopeState State,
        long ConfigVersion,
        string? ConfigPath,
        int NodeCount,
        string? LastError,
        DateTime UpdatedAtUtc);

    /// <summary>
    /// 作用域生命周期：状态转移由 StateHoldDriven 的 ScopeLifecycleStateMachine 实现（纯逻辑），
    /// 渲染段委托给注册的 Provider（FrontEndDriven.State 的 ScopeLifecycleAdapter 实现）。
    /// </summary>
    public interface IScopeLifecycle
    {
        Task<LoadResult> LoadAsync(string scope, string configPath, bool forceReload = false);

        Task DestroyAsync(string scope);

        ScopeLifecycleInfo? GetState(string scope);

        IReadOnlyCollection<string> GetScopes();

        event Action<ScopeLifecycleInfo>? ScopeStateChanged;
    }

    public sealed record LoadResult(
        string Scope,
        bool Succeeded,
        long ConfigVersion,
        int NodeCount,
        string? Error = null);
}
