using Strategy.Observability.Boundary;
using Strategy.Observability.Bus;
using Strategy.Observability.Entity;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Observability;

/// <summary>
/// 观测后端实现（<see cref="IUsharpObservabilityBackend"/>）：把 <c>[Obs]</c> 声明登记为内核契约、
/// 把 <c>[Flow] step emit=</c> 登记为端口发射绑定、产出 manifest，并提供编译期自观测。
/// <para>
/// 内核件复用（不新增第二观测机制）：<see cref="IBoundaryContractRegistry"/>（契约登记，自动桥接事件契约镜像面）
/// + <see cref="Obs"/>（埋点门面 / 关联键作用域）。编译期自观测的事件 kind 属应用侧语义实例，由宿主注册。
/// </para>
/// <para>编译期不写库：manifest 只返回文本；落库由宿主显式 <see cref="IObservabilityBus.FlushAsync"/>。</para>
/// </summary>
public sealed class UsharpObservabilityBackend : IUsharpObservabilityBackend
{
    /// <summary>编译期自观测的实体类型（装配方可覆盖）。</summary>
    public const string DefaultCompilationEntityType = "UsharpCompilation";

    private readonly IBoundaryContractRegistry _boundaries;
    private readonly IObservabilityBus? _bus;
    private readonly string _scopeId;
    private int _seq;

    /// <param name="boundaries">契约注册表（缺省 = 新建内存注册表）。</param>
    /// <param name="bus">观测总线（可空 = 关闭编译期自观测；装配方须先 <c>Obs.Configure(bus)</c>）。</param>
    /// <param name="compilationScopeId">编译期作用域键（缺省 = 按源路径派生的稳定键）。</param>
    public UsharpObservabilityBackend(
        IBoundaryContractRegistry? boundaries = null,
        IObservabilityBus? bus = null,
        string? compilationScopeId = null)
    {
        _boundaries = boundaries ?? new BoundaryContractRegistry();
        _bus = bus;
        _scopeId = string.IsNullOrWhiteSpace(compilationScopeId) ? "usharp.compile" : compilationScopeId;
    }

    /// <summary>装配的契约注册表（宿主可据此查询/校验）。</summary>
    public IBoundaryContractRegistry Boundaries => _boundaries;

    /// <summary>编译期自观测是否启用（装配了总线）。</summary>
    public bool IsCompilationObservationEnabled => _bus is not null;

    public bool IsAvailable => true;

    public void RegisterEvent(UsharpObsDecl decl) => _boundaries.TryRegister(UsharpObsContractMapper.Event(decl));

    public void RegisterScope(UsharpObsDecl decl) => _boundaries.TryRegister(UsharpObsContractMapper.Scope(decl));

    public void RegisterEntity(UsharpObsDecl decl) => _boundaries.TryRegister(UsharpObsContractMapper.Entity(decl));

    public void RegisterFlowEmits(string flowId, string stepKey, IReadOnlyList<string> eventKinds)
        => _boundaries.TryRegister(UsharpObsContractMapper.FlowStep(flowId, stepKey, eventKinds));

    public IDisposable BeginCompilationScope(string sourcePath)
        => _bus is null
            ? NoopScope.Instance
            : Obs.EnterScope(CorrelationBag.New(traceId: sourcePath, sessionId: null, scopeKey: _scopeId));

    public void EmitCompilationEvent(string eventKind, IReadOnlyDictionary<string, double>? metrics = null)
    {
        if (_bus is null || string.IsNullOrWhiteSpace(eventKind)) return;

        var entity = new EntityId(_scopeId, DefaultCompilationEntityType, Interlocked.Increment(ref _seq));
        Obs.Emit(entity, eventKind, builder =>
        {
            if (metrics is null) return;
            foreach (var (key, value) in metrics) builder.WithMetric(key, value);
        });
    }

    public string RenderManifest(UsharpCompilation compilation) => UsharpObsManifestEmitter.Render(compilation);

    /// <summary>空句柄（未装配总线时的编译期作用域）。</summary>
    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        private NoopScope() { }

        public void Dispose() { }
    }
}
