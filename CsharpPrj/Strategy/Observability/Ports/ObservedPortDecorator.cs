using System.Diagnostics;
using Strategy.Observability.Bus;
using Strategy.Observability.Entity;
using Strategy.Observability.Envelope;

namespace Strategy.Observability.Ports;

/// <summary>
/// 观测装饰器（E12）：在装配期统一包裹端口，成对发射 <c>PortInvoked</c> / <c>PortSucceeded</c> / <c>PortFailed</c>
/// （含 <c>elapsed_ms</c>），替代散点埋点，业务实现零改动。
/// <para>
/// 不阻断主路径：埋点异常就地吞；关键路径不调用 <c>FlushAsync</c>；端口自身异常原样抛出（语义不变）。
/// 未配置观测总线时（<see cref="Obs.IsReady"/> = false）自动降级为直通。
/// </para>
/// </summary>
public sealed class ObservedPortDecorator : IPortBinding
{
    /// <summary>调用起始事件分类。</summary>
    public const string InvokedKind = "PortInvoked";

    /// <summary>调用成功事件分类。</summary>
    public const string SucceededKind = "PortSucceeded";

    /// <summary>调用失败事件分类。</summary>
    public const string FailedKind = "PortFailed";

    /// <summary>端口族实体类型（与契约声明的实体类型一致）。</summary>
    public const string EntityType = "Port";

    private readonly string _portKey;
    private readonly IPortBinding _inner;
    private readonly Func<bool>? _enabled;

    /// <summary>
    /// 实体序号分配器单源（AX1）：所有装饰器共享一个进程级工厂，避免不同端口在同一作用域各自从 1 起号
    /// 导致 <c>(Scope, EntityType, Seq)</c> 三元碰撞、事件链串端口。
    /// </summary>
    private static readonly EntityIdFactory SharedIds = new();

    /// <summary><paramref name="enabled"/> 为观测可见性开关（null = 始终可见）。</summary>
    public ObservedPortDecorator(string portKey, IPortBinding inner, Func<bool>? enabled = null)
    {
        _portKey = string.IsNullOrWhiteSpace(portKey) ? throw new ArgumentException("端口键不能为空", nameof(portKey)) : portKey;
        _inner = PortGuard.Required(inner, nameof(inner));
        _enabled = enabled;
    }

    /// <summary>端口键。</summary>
    public string PortKey => _portKey;

    public async ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
    {
        if (_enabled is not null && !_enabled()) return await _inner.InvokeAsync(input, ct).ConfigureAwait(false);

        // AX1：一次调用只分配一个实体（Invoked/Succeeded/Failed 复用同一 id ⇒ 同一条事件链可关联）。
        var entity = Obs.IsReady ? CurrentEntity() : (EntityId?)null;

        var watch = Stopwatch.StartNew();
        Emit(entity, InvokedKind, b => b.Payload(new { port = _portKey, argsHash = input.ArgsHash, src = input.Src }));

        try
        {
            var result = await _inner.InvokeAsync(input, ct).ConfigureAwait(false);
            watch.Stop();
            Emit(entity, SucceededKind, b => b
                .WithMetric("elapsed_ms", watch.Elapsed.TotalMilliseconds)
                .Payload(new { port = _portKey, resultKind = result?.GetType().Name ?? "null" }));
            return result;
        }
        catch (Exception ex)
        {
            watch.Stop();
            Emit(entity, FailedKind, b => b
                .WithMetric("elapsed_ms", watch.Elapsed.TotalMilliseconds)
                .Payload(new { port = _portKey, errorClass = Classify(ex), detail = ex.Message }));
            throw;
        }
    }

    /// <summary>错误分类（纯映射，无动作；分类实现单源 = <see cref="PortErrorClass"/>）。</summary>
    public static string Classify(Exception ex) => PortErrorClass.Of(ex);

    /// <summary>按当前作用域分配本调用的实体（调用方须先确认观测就绪）。</summary>
    private EntityId CurrentEntity()
    {
        var bag = ObsScope.Current;
        var scopeId = bag?.ScopeKey ?? bag?.RunId ?? _portKey;
        return SharedIds.New(scopeId, EntityType);
    }

    /// <summary>发射一条端口族事件（异常就地吞——观测不得阻断主路径）。</summary>
    private static void Emit(EntityId? entity, string kind, Action<ObservabilityEventBuilder> build)
    {
        try
        {
            if (entity is null || !Obs.IsReady) return;
            Obs.Emit(entity.Value, kind, build);
        }
        catch
        {
            // 契约未注册 / 校验失败 / Sink 异常：一律就地吞，不影响端口调用语义。
        }
    }
}
