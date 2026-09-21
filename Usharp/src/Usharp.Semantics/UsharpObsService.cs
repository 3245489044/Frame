using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 观测能力门面：把编译产出的 <c>[Obs]</c> 声明与 <c>[Flow]</c> 步骤发射绑定交给装配的
/// <see cref="IUsharpObservabilityBackend"/>，登记契约并产出 manifest。
/// <para>未装配后端（默认 <see cref="NullObservabilityBackend"/>）时，观测面产出**跳过并显式可见化**
/// （<see cref="UsharpCodes.ObsBackendUnavailable"/>），绝不静默"假装产出"。</para>
/// </summary>
public sealed class UsharpObsService
{
    public UsharpObsService(IUsharpObservabilityBackend? backend = null)
        => Backend = backend ?? NullObservabilityBackend.Instance;

    /// <summary>装配的观测后端（默认空后端）。</summary>
    public IUsharpObservabilityBackend Backend { get; }

    /// <summary>是否已装配真实观测能力。</summary>
    public bool HasBackend => Backend.IsAvailable;

    /// <summary>
    /// 登记 <c>[Obs]</c> 声明与步骤发射绑定；未装配后端时返回单条 <see cref="UsharpCodes.ObsBackendUnavailable"/> 可见化诊断。
    /// </summary>
    public IReadOnlyList<UsharpDiagnostic> Analyze(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        if (!HasBackend)
            return new[]
            {
                UsharpDiagnostic.Info(UsharpCodes.ObsBackendUnavailable,
                    "未装配观测后端（NullObservabilityBackend）：契约登记与 manifest 产出跳过；"
                    + "[Obs] 闸A、引用即边与结构化诊断不受影响",
                    compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 0),
            };

        foreach (var scope in compilation.Obs.Scopes) Backend.RegisterScope(scope);
        foreach (var entity in compilation.Obs.Entities) Backend.RegisterEntity(entity);
        foreach (var @event in compilation.Obs.Events) Backend.RegisterEvent(@event);

        foreach (var flow in compilation.Flows)
            foreach (var step in flow.Steps)
                if (step.Emits.Count > 0)
                    Backend.RegisterFlowEmits(flow.FlowId, step.Key, step.Emits);

        return Array.Empty<UsharpDiagnostic>();
    }

    /// <summary>确定性 manifest 产出（未装配后端 → 可见化提示，非 JSON）。</summary>
    public string RenderManifest(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return Backend.RenderManifest(compilation);
    }
}
