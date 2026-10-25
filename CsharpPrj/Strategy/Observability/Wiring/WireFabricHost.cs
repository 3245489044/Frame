using Modules.Declaration;
using Modules.Wiring.Compile;
using Modules.Wiring.Declaration;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;
using Modules.Wiring.Runtime;
using Strategy.Observability.Ports;

namespace Strategy.Observability.Wiring;

/// <summary>Attach 产物：冻结 plan + 唯一投递点 + 全量报告（含装载期与编译期违规）。</summary>
public sealed record WireFabricAttachment(
    FrozenWiringPlan Frozen,
    Junction Junction,
    WiringReport Report)
{
    public bool IsClosed => Report.IsClosed;

    public WiringPlan Plan => Frozen.Plan;
}

/// <summary>
/// Attach 阶段门面（装配期一次）：声明文本 → 投影 → 校验 → plan → 冻结 → 绑定 <see cref="CallLog"/> 账本。
/// <para>运行期零解析：下游只持有 <see cref="Junction"/>；未闭合 plan 由 <see cref="AttachOrThrow"/> fail-fast。</para>
/// </summary>
public static class WireFabricHost
{
    public static WireFabricAttachment Attach(
        string declarationJson,
        string sourceId,
        CallLog log,
        string generation = WiringFreezer.LiveGeneration)
    {
        ArgumentNullException.ThrowIfNull(log);

        var loaded = JsonWiringDeclarationSource.Load(declarationJson, sourceId);
        var catalog = WiringCatalogFactory.FromSource(loaded.Source);
        var plan = WiringPlanner.Plan(catalog);

        var violations = new List<WiringViolation>(loaded.Violations);
        violations.AddRange(plan.Violations);

        var frozen = WiringFreezer.Freeze(plan, generation);
        var junction = new Junction(plan, new CallLogWireLedger(log), generation);

        return new WireFabricAttachment(frozen, junction, new WiringReport(violations));
    }

    /// <summary>生产姿态：未闭合即抛（Report/Throw 复用 <see cref="ClosureStrictness"/> 单源）。</summary>
    public static WireFabricAttachment AttachOrThrow(
        string declarationJson,
        string sourceId,
        CallLog log,
        ClosureStrictness strictness = ClosureStrictness.Throw,
        string generation = WiringFreezer.LiveGeneration)
    {
        var attachment = Attach(declarationJson, sourceId, log, generation);
        attachment.Report.Enforce(strictness, sourceId);
        return attachment;
    }
}
