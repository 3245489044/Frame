using Strategy.Observability.Boundary;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Observability;

/// <summary>
/// 观测后端实现（<see cref="IUsharpObservabilityBackend"/>）：把 <c>[Obs]</c> 声明登记为内核契约、
/// 把 <c>[Flow] step emit=</c> 登记为端口发射绑定、产出 manifest。
/// <para>
/// 内核件复用（不新增第二观测机制）：<see cref="IBoundaryContractRegistry"/>（契约登记，自动桥接事件契约镜像面）。
/// </para>
/// <para>编译期不写库（H3）：本实现不持总线、不发事件 —— 端口面不再提供编译期自观测（无消费方），
/// 落库一律由宿主显式 <c>IObservabilityBus.FlushAsync</c>。</para>
/// <para>
/// 唯一引点（D）：本文件 = Observability 子域**唯一引点**（唯一引观测处，README §9.5）；
/// 领域件引 Usharp 一律经本子域适配件，禁跨子域直引。
/// </para>
/// </summary>
public sealed class UsharpObservabilityBackend : IUsharpObservabilityBackend
{
    private readonly IBoundaryContractRegistry _boundaries;

    /// <param name="boundaries">契约注册表（缺省 = 新建内存注册表）。</param>
    public UsharpObservabilityBackend(IBoundaryContractRegistry? boundaries = null)
        => _boundaries = boundaries ?? new BoundaryContractRegistry();

    /// <summary>装配的契约注册表（宿主可据此查询/校验）。</summary>
    public IBoundaryContractRegistry Boundaries => _boundaries;

    /// <summary>
    /// 登记结果台账：<c>TryRegister</c> 的返回值不再丢弃（E-14）；冲突可在宿主侧读取并上报。
    /// </summary>
    public IReadOnlyList<UsharpRegistrationResult> Registrations => _registrations;

    public bool IsAvailable => true;

    public void RegisterEvent(UsharpObsDecl decl) => Register(_boundaries.TryRegister(UsharpObsContractMapper.Event(decl)));

    public void RegisterScope(UsharpObsDecl decl) => Register(_boundaries.TryRegister(UsharpObsContractMapper.Scope(decl)));

    public void RegisterEntity(UsharpObsDecl decl) => Register(_boundaries.TryRegister(UsharpObsContractMapper.Entity(decl)));

    public void RegisterFlowEmits(string flowId, string stepKey, IReadOnlyList<string> eventKinds)
        => Register(_boundaries.TryRegister(UsharpObsContractMapper.FlowStep(flowId, stepKey, eventKinds)));

    private readonly List<UsharpRegistrationResult> _registrations = new();

    private void Register(bool registered)
        => _registrations.Add(registered
            ? UsharpRegistrationResult.Success
            : UsharpRegistrationResult.Conflict(null));

    public string RenderManifest(UsharpCompilation compilation) => UsharpObsManifestEmitter.Render(compilation);
}
