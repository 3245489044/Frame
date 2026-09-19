using Strategy.Observability.Projection;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 环境作用域提供者的**委托载具**：领域侧只交一行取值函数，不必自建 <see cref="ISpecScopeProvider"/> 实现
/// （无作用域 → null，宿主据此报「run/round 未进入」，不伪造参数）。
/// </summary>
public static class SpecScopeProviders
{
    /// <summary>按环境取值函数构造提供者（<c>AgentTrace.AmbientScopeId()</c> 类的一行接线）。</summary>
    public static ISpecScopeProvider Ambient(Func<string?> ambientScopeId)
        => new DelegateScopeProvider(ambientScopeId ?? throw new ArgumentNullException(nameof(ambientScopeId)));

    private sealed class DelegateScopeProvider(Func<string?> ambientScopeId) : ISpecScopeProvider
    {
        public string? AmbientScopeId() => ambientScopeId();
    }
}

/// <summary>
/// 观测事件源的**委托载具**：读前排空（flush）与资源释放仍由实现侧承担（G-3：关键路径禁 Flush，
/// 仅用例出口例外），本类只负责「未装配即抛」的 fail-fast 与释放的幂等收敛。
/// </summary>
public static class SpecEventSources
{
    /// <summary>
    /// <paramref name="read"/>：读取作用域事件序（实现侧负责读前排空与生命周期）；
    /// <paramref name="available"/> = false → <see cref="ISpecEventSource.Read"/> 抛可定位异常（不静默空集）；
    /// <paramref name="dispose"/>：释放领域侧自装配资源（缺省无资源可放）。
    /// </summary>
    public static ISpecEventSource From(
        Func<string, IReadOnlyList<ObservationOutcome>> read, bool available = true, Action? dispose = null)
        => new DelegateEventSource(
            read ?? throw new ArgumentNullException(nameof(read)), available, dispose);

    private sealed class DelegateEventSource : ISpecEventSource
    {
        private readonly Func<string, IReadOnlyList<ObservationOutcome>> _read;
        private readonly Action? _dispose;
        private bool _disposed;

        internal DelegateEventSource(
            Func<string, IReadOnlyList<ObservationOutcome>> read, bool available, Action? dispose)
        {
            _read = read;
            _dispose = dispose;
            IsAvailable = available;
        }

        public bool IsAvailable { get; }

        public IReadOnlyList<ObservationOutcome> Read(string scopeId)
            => IsAvailable
                ? _read(scopeId)
                : throw new InvalidOperationException(
                    "[spec:port] 观测事件源未装配：请在装配处传 SpecPortOptions.ObserveEventStorePath（或改用 Port(…) 根）。");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _dispose?.Invoke();
        }
    }
}
