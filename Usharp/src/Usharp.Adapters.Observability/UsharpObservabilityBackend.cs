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
/// </summary>
public sealed class UsharpObservabilityBackend : IUsharpObservabilityBackend
{
    private readonly IBoundaryContractRegistry _boundaries;

    /// <param name="boundaries">契约注册表（缺省 = 新建内存注册表）。</param>
    public UsharpObservabilityBackend(IBoundaryContractRegistry? boundaries = null)
        => _boundaries = boundaries ?? new BoundaryContractRegistry();

    /// <summary>装配的契约注册表（宿主可据此查询/校验）。</summary>
    public IBoundaryContractRegistry Boundaries => _boundaries;

    public bool IsAvailable => true;

    public void RegisterEvent(UsharpObsDecl decl) => _boundaries.TryRegister(UsharpObsContractMapper.Event(decl));

    public void RegisterScope(UsharpObsDecl decl) => _boundaries.TryRegister(UsharpObsContractMapper.Scope(decl));

    public void RegisterEntity(UsharpObsDecl decl) => _boundaries.TryRegister(UsharpObsContractMapper.Entity(decl));

    public void RegisterFlowEmits(string flowId, string stepKey, IReadOnlyList<string> eventKinds)
        => _boundaries.TryRegister(UsharpObsContractMapper.FlowStep(flowId, stepKey, eventKinds));

    public string RenderManifest(UsharpCompilation compilation) => UsharpObsManifestEmitter.Render(compilation);
}
