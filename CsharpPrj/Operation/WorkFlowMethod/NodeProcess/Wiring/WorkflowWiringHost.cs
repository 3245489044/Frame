using System;
using System.Threading;
using Modules.KeySpace.Abstractions;
using Modules.Wiring.Compile;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;
using Modules.Wiring.Runtime;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using ChainModel = GraphFramework.Models.Chain<string, WorkFlowMethod.Skeletons.NodeProcess.Models.NodeConfig>;

namespace WorkFlowMethod.Skeletons.NodeProcess.Wiring;

/// <summary>装配期接线产物：冻结 plan + 唯一投递点 + 全量报告 + 目录。</summary>
public sealed record WorkflowWiringAttachment(
    FrozenWiringPlan Frozen,
    Junction Junction,
    WiringReport Report,
    WiringCatalog Catalog)
{
    public bool IsClosed => Report.IsClosed;
    public WiringPlan Plan => Frozen.Plan;
}

/// <summary>
/// B1/B2：WFM 侧接线宿主（组合根一次调用）。
/// <para>
/// 默认 <see cref="WireDeliveryMode.Shadow"/>：只判定与记账，不执行实现、不产生值流副作用；
/// 闭合报告用于装配期验收。切换到 Primary 必须显式传参（并先确认端口映射/端口仓桥接）。
/// </para>
/// </summary>
public static class WorkflowWiringHost
{
    public static WorkflowWiringAttachment Attach(
        ChainModel? chain,
        string sourceId = "workflow",
        IKeySpaceDeclaration? declaration = null,
        IWireLedger? ledger = null,
        string generation = WiringFreezer.LiveGeneration,
        WireDeliveryMode mode = WireDeliveryMode.Shadow,
        IWirePortStore? portStore = null,
        IWireHandlerTable? handlers = null)
    {
        var catalog = WorkflowWiringCatalogBuilder.Build(sourceId, chain, declaration);
        return Attach(catalog, ledger, generation, mode, portStore, handlers);
    }

    public static WorkflowWiringAttachment Attach(
        WiringCatalog catalog,
        IWireLedger? ledger = null,
        string generation = WiringFreezer.LiveGeneration,
        WireDeliveryMode mode = WireDeliveryMode.Shadow,
        IWirePortStore? portStore = null,
        IWireHandlerTable? handlers = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var plan = WiringPlanner.Plan(catalog);
        var frozen = WiringFreezer.Freeze(plan, generation);
        var junction = new Junction(
            plan,
            ledger ?? WorkflowMethodLogWireLedger.Instance,
            generation,
            adapters: null,
            postbox: null,
            handlers: handlers ?? WireHandlerTable.Empty,
            mode: mode,
            portStore: portStore ?? new InMemoryWirePortStore());

        return new WorkflowWiringAttachment(frozen, junction, new WiringReport(plan.Violations), catalog);
    }
}

/// <summary>
/// B4：投递账 → 统一日志（WFM 内唯一投影；宿主可替换为 ur_events 适配器）。
/// 一条 delivery 恰一条结构化记录，不散落 <c>WorkFlowMethodLog</c> 调用。
/// </summary>
public sealed class WorkflowMethodLogWireLedger : IWireLedger
{
    public static readonly WorkflowMethodLogWireLedger Instance = new();

    public void Record(WireLedgerRecord record)
    {
        if (record is null) return;
        WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log("wire.delivery", new
        {
            wireId = record.WireId,
            opId = record.OpId,
            form = record.Form,
            length = record.Length,
            status = record.Status.ToString(),
            reason = record.Reason,
            generation = record.Generation,
            scope = record.Scope,
            port = record.Port,
        });
    }
}
