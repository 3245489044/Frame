using BasicMethod.Initialization.Errors;
using BasicMethod.Initialization.Guards;
using BasicMethod.Initialization.Modules;

namespace BasicMethod.Initialization;

/// <summary>
/// 可初始化组件基类：统一端口校验 + 生命周期，消除每类各自手写 <c>?? throw</c>。
/// <para>必填端口经 <see cref="Require{T}"/> 登记缺失（<b>不就地抛</b>），在初始化末尾 <see cref="AssertPortsComplete"/>
/// 一次性 fail-fast（可诊断、可归因）。因此组件须在初始化完成后方可对外使用。</para>
/// </summary>
public abstract class InitializableBase : IModuleNode
{
    private readonly List<string> _missing = new();
    private InitState _state = InitState.Created;

    /// <inheritdoc />
    public InitState State => _state;

    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <inheritdoc />
    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    /// <inheritdoc />
    public virtual int Order => 0;

    /// <summary>
    /// T：解析必填端口；null 即登记缺失（装配期统一 fail-fast，而非就地抛）。
    /// 返回 <c>null!</c> 以延后到 <see cref="AssertPortsComplete"/> 汇总报告——缺失值在初始化完成前不得被使用。
    /// </summary>
    protected T Require<T>(T? value, string name) where T : class
    {
        if (value is null) { _missing.Add(name); return null!; }
        return value;
    }

    /// <summary>N：解析可选端口；缺省用命名空对象（调用方提供 Null 实现）。</summary>
    protected T Optional<T>(T? value, T nullObject) where T : class
        => value ?? nullObject;

    /// <summary>装配期自检：任一必填缺失 → 一次性 fail-fast。</summary>
    protected void AssertPortsComplete() => PortGuard.PortsComplete(Name, _missing);

    /// <inheritdoc />
    public async ValueTask InitializeAsync(CancellationToken ct)
    {
        if (_state != InitState.Created) return;
        _state = InitState.Initializing;
        try
        {
            await OnInitializeAsync(ct).ConfigureAwait(false);
            AssertPortsComplete();
            _state = InitState.Ready;
        }
        catch
        {
            _state = InitState.Failed;
            throw;
        }
    }

    /// <inheritdoc />
    public virtual ValueTask ShutdownAsync(CancellationToken ct)
    {
        _state = InitState.Disposed;
        return ValueTask.CompletedTask;
    }

    /// <summary>子类初始化钩子。</summary>
    protected virtual ValueTask OnInitializeAsync(CancellationToken ct) => ValueTask.CompletedTask;
}
