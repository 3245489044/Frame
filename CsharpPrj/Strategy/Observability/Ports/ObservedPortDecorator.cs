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
    private readonly EntityIdFactory _ids = new();

    /// <summary><paramref name="enabled"/> 为观测可见性开关（null = 始终可见）。</summary>
    public ObservedPortDecorator(string portKey, IPortBinding inner, Func<bool>? enabled = null)
    {
        _portKey = string.IsNullOrWhiteSpace(portKey) ? throw new ArgumentException("端口键不能为空", nameof(portKey)) : portKey;
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _enabled = enabled;
    }

    /// <summary>端口键。</summary>
    public string PortKey => _portKey;

    public async ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
    {
        if (_enabled is not null && !_enabled()) return await _inner.InvokeAsync(input, ct).ConfigureAwait(false);

        var watch = Stopwatch.StartNew();
        Emit(InvokedKind, b => b.Payload(new { port = _portKey, argsHash = input.ArgsHash, src = input.Src }));

        try
        {
            var result = await _inner.InvokeAsync(input, ct).ConfigureAwait(false);
            watch.Stop();
            Emit(SucceededKind, b => b
                .WithMetric("elapsed_ms", watch.Elapsed.TotalMilliseconds)
                .Payload(new { port = _portKey, resultKind = result?.GetType().Name ?? "null" }));
            return result;
        }
        catch (Exception ex)
        {
            watch.Stop();
            Emit(FailedKind, b => b
                .WithMetric("elapsed_ms", watch.Elapsed.TotalMilliseconds)
                .Payload(new { port = _portKey, errorClass = Classify(ex), detail = ex.Message }));
            throw;
        }
    }

    /// <summary>错误分类（纯映射，无动作；分类实现单源 = <see cref="PortErrorClass"/>）。</summary>
    public static string Classify(Exception ex) => PortErrorClass.Of(ex);

    /// <summary>发射一条端口族事件（异常就地吞——观测不得阻断主路径）。</summary>
    private void Emit(string kind, Action<ObservabilityEventBuilder> build)
    {
        try
        {
            if (!Obs.IsReady) return;
            var bag = ObsScope.Current;
            var scopeId = bag?.ScopeKey ?? bag?.RunId ?? _portKey;
            Obs.Emit(_ids.New(scopeId, EntityType), kind, build);
        }
        catch
        {
            // 契约未注册 / 校验失败 / Sink 异常：一律就地吞，不影响端口调用语义。
        }
    }
}
