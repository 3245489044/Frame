using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Identity;
using Modules.Node.Spec;

namespace Modules.Node;

/// <summary>单域装配事实（装配成功一次一条；供调用方聚合为单条摘要）。</summary>
/// <param name="DomainKey">域键。</param>
/// <param name="Placements">位置数。</param>
/// <param name="Carriers">载体数。</param>
/// <param name="Errors">声明诊断 Error 数。</param>
/// <param name="Warnings">声明诊断 Warning 数。</param>
public sealed record NodeDomainAssemblyFact(
    string DomainKey,
    int Placements,
    int Carriers,
    int Errors,
    int Warnings);

/// <summary>
/// 域装配门面的装配端口集（机制与数据分离：装配流水线唯一实现，差异全在此）。
/// </summary>
public sealed class NodeFrameworkAssemblyPorts
{
    /// <summary>域描述（框架基底；上层派生记录追加载体工厂/事件名）。</summary>
    public required NodeDomainSchema Descriptor { get; init; }

    /// <summary>声明装载（未命中 ⇒ <c>null</c> + 可见化，不抛）。</summary>
    public required Func<NodeFrameworkSpec?> LoadSpec { get; init; }

    /// <summary>载体注册表工厂（声明驱动分派，去 per-domain switch）。</summary>
    public required Func<NodeFrameworkSpec, ICarrierRegistry> CreateCarriers { get; init; }

    /// <summary>诊断接收端（声明装载与框架运行时同源）。</summary>
    public INodeDiagnosticsSink? Diagnostics { get; init; }

    /// <summary>序数权威（仅 <see cref="NodeDomainSchema.InjectSequenceAuthority"/> 为真的域注入）。</summary>
    public ISequenceAuthority? SequenceAuthority { get; init; }

    /// <summary>运行域供体（仅 <see cref="NodeDomainSchema.RuntimeBound"/> 域接入；一个供体多域共用）。</summary>
    public INodeRunScopeSource? RunScopeSource { get; init; }

    /// <summary>取用词表端口工厂（缺件即抛；缺省不注入）。</summary>
    public Func<ISelectionVocabulary?>? SelectionVocabulary { get; init; }

    /// <summary>定义期诊断端口（缺省不注入）。</summary>
    public IDefinitionDiagnostics? DefinitionDiagnostics { get; init; }

    /// <summary>宿主日志端口（缺省不落日志）。</summary>
    public INodeFrameworkLog? Log { get; init; }

    /// <summary>声明装载成功取证事件名。</summary>
    public string LoadedEvent { get; init; } = "node-framework:declaration-loaded";

    /// <summary>带诊断的声明装载取证事件名（errors/warnings&gt;0）。</summary>
    public string LoadedWithIssuesEvent { get; init; } = "node-framework:declaration-loaded-issues";

    /// <summary>装配成功回调（单域一条事实；缺省不回调）。</summary>
    public Action<NodeDomainAssemblyFact>? OnAssembled { get; init; }
}

/// <summary>
/// 节点域**通用装配门面**（唯一实现）—— 所有域共用同一段装配逻辑，差异**只来自数据**
/// （<see cref="NodeFrameworkAssemblyPorts"/>）：声明装载 → 载体注册 → <see cref="NodeFramework.FromSpec"/>
/// → 装配校验 → 取证日志 → 装配事实。
/// <para>
/// 存在理由（SingleImplementation / RedundancyElimination）：各域的装配机制完全相同，仅参数不同；
/// 新增域 = 上层域目录增一行，装配逻辑零改动（禁再为单域复制一遍装配代码）。
/// </para>
/// <para>
/// 序数权威：仅 <see cref="NodeDomainSchema.InjectSequenceAuthority"/> 为真的域注入
/// <see cref="ISequenceAuthority"/>；其余域的身份序数由调用方按源索引 / 到达序给出。
/// </para>
/// </summary>
public static class NodeFrameworkAssembly
{
    /// <summary>按域描述装配节点框架（唯一实现）。声明缺失 ⇒ <c>null</c>（不兜底）。</summary>
    public static NodeFramework? TryCreate(NodeFrameworkAssemblyPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var descriptor = ports.Descriptor;

        var spec = ports.LoadSpec();
        if (spec is null)
        {
            return null;
        }

        var registry = ports.CreateCarriers(spec);
        // 运行域供体：仅运行态面域（RuntimeBound）接入 —— 一个供体、多域共用；未接入 ⇒ Null 实现 ⇒ no-op。
        var runScope = descriptor.RuntimeBound ? ports.RunScopeSource : null;
        // requireRunScope：运行态面域启用 ⇒ 运行域缺失（未经 BeginRun）的写入被框架拒写，而非静默接受。
        var requireRunScope = descriptor.RuntimeBound;
        var vocabulary = ports.SelectionVocabulary?.Invoke();

        var framework = NodeFramework.FromSpec(
            spec,
            registry,
            ports.Diagnostics,
            descriptor.InjectSequenceAuthority ? ports.SequenceAuthority : null,
            runScope,
            requireRunScope,
            specSource: null,
            definitionDiagnostics: ports.DefinitionDiagnostics,
            selectionVocabulary: vocabulary);

        // 声明判据**装配期**生效（fail-fast）：静默装配会让位置键在运行期按未声明字段渲染出漂移键。
        var validation = framework.Validator.Validate(spec);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"节点域 '{descriptor.DomainKey}' 声明非法（{validation.Errors.Count} 项）："
                + string.Join("；", validation.Errors.Select(e => $"{e.Code}: {e.Message}")));
        }

        var errors = spec.Diagnostics.Count(d => d.Severity == NodeDiagnosticSeverity.Error);
        var warnings = spec.Diagnostics.Count(d => d.Severity == NodeDiagnosticSeverity.Warning);

        // 取证聚合：无诊断 → 常规事件；有诊断 → 带问题事件（errors/warnings>0 仍明细，不掩错）。
        ports.Log?.Log(
            errors > 0 || warnings > 0 ? ports.LoadedWithIssuesEvent : ports.LoadedEvent,
            new
            {
                domainKey = descriptor.DomainKey,
                subPath = descriptor.SubPath,
                frameworkId = framework.FrameworkId,
                domain = spec.Domain,
                identity = framework.Identity.Template,
                placements = framework.Placements.Count,
                writablePlacements = framework.Placements.All.Count(p => p.IsWritable),
                readablePlacements = framework.Placements.All.Count(p => p.IsReadable),
                carriers = registry.All.Count,
                errors,
                warnings,
                sequenceAuthority = descriptor.InjectSequenceAuthority ? "bound" : "null（序数由调用方给出）",
                requireRunScope = requireRunScope ? "on（运行态面域：无 run 即拒写）" : "off（无运行域语义）",
            });

        ports.OnAssembled?.Invoke(new NodeDomainAssemblyFact(
            descriptor.DomainKey, framework.Placements.Count, registry.All.Count, errors, warnings));

        return framework;
    }
}
