using System.Collections.Generic;
using System.Threading.Tasks;
using Operation.ReconcileDriven.Host;
using Share.EventBindingOrchestrator.Reconcile;
using TConfig;

namespace Operation.ReconcileDriven.Host;

/// <summary>
/// 对账钩子实现（<see cref="IEventReconcileHook"/> 的 Operation 侧实现）：
/// 绑定期登记契约到 <see cref="ReconcileContractRegistry"/>；触发期按函数名解析契约后委托 <see cref="ReconcileStage"/>。
/// <para>组合根经 <see cref="ReconcileBootstrap"/> 注入 <c>InvocationKernel.ReconcileHook</c>。</para>
/// </summary>
public sealed class ReconcileHook : IEventReconcileHook
{
    private readonly ReconcileContractRegistry _registry;
    private readonly ReconcileStage _stage;
    private readonly ReconcileOptions _options;

    public ReconcileHook(ReconcileContractRegistry registry, ReconcileStage stage, ReconcileOptions? options = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _options = options ?? new ReconcileOptions();
    }

    /// <inheritdoc />
    public bool Enabled => _options.Enabled;

    /// <inheritdoc />
    public void RegisterContract(string functionName, ReconcileConfig contract)
        => _registry.Register(functionName, contract);

    /// <inheritdoc />
    public Task<object?> InvokeAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        Func<Task<object?>> invoke)
    {
        _registry.TryGet(functionName, out var contract);
        return _stage.RunAsync(contract, functionName, invoke);
    }
}
