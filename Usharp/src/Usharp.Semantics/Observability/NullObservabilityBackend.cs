using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 空观测后端（默认实现）：全 no-op —— 登记不落任何契约、作用域返回空句柄、编译事件丢弃、manifest 不产出。
/// 语义：**未装配观测实现 = 纯声明校验模式**，<c>[Obs]</c> 闸A、引用即边与结构化诊断仍完整可用；
/// 观测面判据的缺失经 <see cref="UsharpCodes.ObsBackendUnavailable"/> 显式可见化（不静默、不假装通过）。
/// </summary>
public sealed class NullObservabilityBackend : IUsharpObservabilityBackend
{
    /// <summary>单例（无状态）。</summary>
    public static readonly NullObservabilityBackend Instance = new();

    private NullObservabilityBackend() { }

    public bool IsAvailable => false;

    public void RegisterEvent(UsharpObsDecl decl) { }

    public void RegisterScope(UsharpObsDecl decl) { }

    public void RegisterEntity(UsharpObsDecl decl) { }

    public void RegisterFlowEmits(string flowId, string stepKey, IReadOnlyList<string> eventKinds) { }

    public string RenderManifest(UsharpCompilation compilation)
        => $"{UsharpCodes.ObsBackendUnavailable} 未装配观测后端（NullObservabilityBackend）：契约产出与编译期自观测跳过；"
         + "闸A、引用即边与结构化诊断不受影响（显式可见化，不假装通过）";
}
