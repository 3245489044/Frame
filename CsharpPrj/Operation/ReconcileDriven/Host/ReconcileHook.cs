using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Operation.ReconcileDriven.Gates;
using Share.EventBindingOrchestrator.Reconcile;
using TConfig;

namespace Operation.ReconcileDriven.Host;

/// <summary>
/// 对账钩子实现（<see cref="IEventReconcileHook"/> 的 Operation 侧实现）：
/// 绑定期登记契约到 <see cref="ReconcileContractRegistry"/>（可选经 <see cref="ReconcileContractGate"/> 拒载不合格声明）；
/// 触发期按「函数名 + 载荷 <c>cmd</c>」解析契约后委托 <see cref="ReconcileStage"/>。
/// <para>组合根经 <see cref="ReconcileBootstrap"/> 注入 <c>InvocationKernel.ReconcileHook</c>。</para>
/// </summary>
public sealed class ReconcileHook : IEventReconcileHook
{
    private readonly ReconcileContractRegistry _registry;
    private readonly ReconcileStage _stage;
    private readonly ReconcileOptions _options;
    private readonly ReconcileContractGate? _gate;

    public ReconcileHook(
        ReconcileContractRegistry registry,
        ReconcileStage stage,
        ReconcileOptions? options = null,
        ReconcileContractGate? gate = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _options = options ?? new ReconcileOptions();
        _gate = gate;
    }

    /// <inheritdoc />
    public bool Enabled => _options.Enabled;

    /// <inheritdoc />
    public void RegisterContract(string functionName, ReconcileConfig contract)
    {
        // 装载期判据 C1/C2/C6：不合格声明当场拒载（可见于绑定期错误收集，不静默）。
        if (_gate is not null && !_gate.TryValidate(contract, out var error))
            throw new InvalidOperationException($"对账契约装载拒载：function='{functionName}' {error}");

        _registry.Register(functionName, contract);
    }

    /// <inheritdoc />
    public Task<object?> InvokeAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        Func<Task<object?>> invoke)
    {
        var command = ExtractCommand(parameters);
        _registry.TryGet(functionName, command, out var contract);
        return _stage.RunAsync(contract, functionName, invoke);
    }

    /// <summary>
    /// 从事件参数提取命令（消息桥多路复用）：载荷键 <c>payload</c> 可为 JSON 串（<c>{"cmd":"..."}</c>）
    /// 或字典对象；解析失败/无 cmd ⇒ null（回退默认契约）。
    /// </summary>
    private static string? ExtractCommand(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue("payload", out var raw) || raw is null)
            return null;

        if (raw is string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                       && doc.RootElement.TryGetProperty("cmd", out var cmd)
                       && cmd.ValueKind == JsonValueKind.String
                    ? cmd.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;   // 非法载荷：不因对账而阻断主路径
            }
        }

        if (raw is IReadOnlyDictionary<string, object?> map && map.TryGetValue("cmd", out var value))
            return value?.ToString();

        return null;
    }
}
