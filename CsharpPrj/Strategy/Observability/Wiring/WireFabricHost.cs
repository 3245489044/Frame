using Modules.Declaration;
using Modules.Wiring.Compile;
using Modules.Wiring.Declaration;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;
using Modules.Wiring.Runtime;
using Strategy.Observability.Ports;

namespace Strategy.Observability.Wiring;

/// <summary>Attach 产物：冻结 plan + 唯一投递点 + 全量报告 + 已编译目录（含盒/端口，供组合根注册 handler）。</summary>
public sealed record WireFabricAttachment(
    FrozenWiringPlan Frozen,
    Junction Junction,
    WiringReport Report,
    WiringCatalog Catalog)
{
    public bool IsClosed => Report.IsClosed;

    public WiringPlan Plan => Frozen.Plan;

    public IWirePortStore PortStore => Junction.PortStore;
}

/// <summary>
/// Attach 阶段门面（装配期一次）：声明文本 → 投影 → 校验 → plan → 冻结 → 绑定账本/适配器/handler。
/// <para>运行期零解析：下游只持有 <see cref="Junction"/>；未闭合 plan 由 <see cref="AttachOrThrow"/> fail-fast。</para>
/// </summary>
public static class WireFabricHost
{
    public static WireFabricAttachment Attach(
        string declarationJson,
        string sourceId,
        CallLog log,
        string generation = WiringFreezer.LiveGeneration)
        => Attach(declarationJson, sourceId, new CallLogWireLedger(log), generation: generation);

    public static WireFabricAttachment Attach(
        string declarationJson,
        string sourceId,
        IWireLedger ledger,
        IReadOnlyDictionary<string, Func<ValueEnvelope, ValueEnvelope>>? adapters = null,
        IWireHandlerTable? handlers = null,
        string? profile = null,
        string generation = WiringFreezer.LiveGeneration,
        WireDeliveryMode mode = WireDeliveryMode.Primary,
        IWirePortStore? portStore = null,
        bool strictGeneration = false,
        Func<string>? liveGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var loaded = JsonWiringDeclarationSource.Load(declarationJson, sourceId);
        var catalog = WiringCatalogFactory.FromSource(loaded.Source);

        var violations = new List<WiringViolation>(loaded.Violations);
        if (!WiringCatalogFilter.TryApplyProfile(catalog, profile, out catalog, out var profileError))
        {
            violations.Add(WiringViolation.Create(
                WiringReasonCodes.WireProfileUnknown, profile ?? string.Empty,
                "剖面未知（未启用任何子集）", "profile=" + profile, "error=" + profileError));
        }

        var plan = WiringPlanner.Plan(catalog);
        violations.AddRange(plan.Violations);

        var frozen = WiringFreezer.Freeze(plan, generation);
        var junction = new Junction(
            plan, ledger, generation, adapters, postbox: null, handlers, mode,
            portStore, strictGeneration, liveGeneration);

        return new WireFabricAttachment(frozen, junction, new WiringReport(violations), catalog);
    }

    public static WireFabricAttachment AttachFromFile(
        string declarationPath,
        string sourceId,
        IWireLedger ledger,
        IReadOnlyDictionary<string, Func<ValueEnvelope, ValueEnvelope>>? adapters = null,
        IWireHandlerTable? handlers = null,
        string? profile = null,
        string generation = WiringFreezer.LiveGeneration,
        WireDeliveryMode mode = WireDeliveryMode.Primary,
        IWirePortStore? portStore = null,
        bool strictGeneration = false,
        Func<string>? liveGeneration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declarationPath);
        var json = File.ReadAllText(declarationPath);
        return Attach(json, sourceId, ledger, adapters, handlers, profile, generation, mode,
            portStore, strictGeneration, liveGeneration);
    }

    /// <summary>生产姿态：未闭合即抛（Report/Throw 复用 <see cref="ClosureStrictness"/> 单源）。</summary>
    public static WireFabricAttachment AttachOrThrow(
        string declarationJson,
        string sourceId,
        CallLog log,
        ClosureStrictness strictness = ClosureStrictness.Throw,
        string generation = WiringFreezer.LiveGeneration)
        => AttachOrThrow(declarationJson, sourceId, new CallLogWireLedger(log), strictness: strictness, generation: generation);

    public static WireFabricAttachment AttachOrThrow(
        string declarationJson,
        string sourceId,
        IWireLedger ledger,
        IReadOnlyDictionary<string, Func<ValueEnvelope, ValueEnvelope>>? adapters = null,
        IWireHandlerTable? handlers = null,
        string? profile = null,
        ClosureStrictness strictness = ClosureStrictness.Throw,
        string generation = WiringFreezer.LiveGeneration,
        WireDeliveryMode mode = WireDeliveryMode.Primary)
    {
        var attachment = Attach(declarationJson, sourceId, ledger, adapters, handlers, profile, generation, mode);
        attachment.Report.Enforce(strictness, sourceId);
        return attachment;
    }
}

