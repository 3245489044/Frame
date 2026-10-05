using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Ports;
using Modules.Threading.Streaming;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.ReferenceSchema;
using Operation.HierarchyMethod.Abstractions;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.Nodes;
using Operation.WorkFlowMethod.Nodes.Contracts;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 【节点持久化 / 引用域装配切片】(canonical，SingleImplementation / TH-12)。
/// 聚合 Nodes 叶子依赖 + 运行旋钮，消掉各构造（轮执行器 / 节点存储 / 执行服务）反复重开的"可选参袋"——
/// 一袋承载节点持久化栈 / 引用域全部可选注入与执行旋钮；null = 该增强路径未装配/回退。
/// 自 Agent.Core 下沉框架（写侧唯一写门所在层），Agent 侧只做装配注入。
/// </summary>
public sealed record NodePersistenceAssembly(
    INodeStore? NodeStore = null,
    IKeyMappingStore? KeyMappings = null,
    ReferenceGrounding? Grounding = null,
    IReferenceTypeRegistry? ReferenceRegistry = null,
    IHistoryProjector? HistoryProjector = null,
    // 元素类型对齐共享端口 IPersistenceStore.BeginTransactionAsync 的非空返回真值（§2.3 收敛，非 ITransaction?）。
    Func<CancellationToken, Task<ITransaction>>? BeginTransaction = null,
    bool EnableOutbox = false,
    TimeSpan? OutboxFlushTimeout = null,
    TimeSpan StepTimeout = default,
    // DP-3（W2-0）双写收敛开关：默认关 = 旧路径（执行期扁平 vars 写入保留，行为等价）；
    // 开 = 写侧单写 ur_nodes（权威） + 读侧投影重算（自权威回填），vars 不再作为同一事实的第二写点。
    bool EnableUnifiedStateWrite = false,
    // 归属边簿记流入口（R1，可空）：装配后节点执行期的 task→node 归属边经**声明流**写（唯一编排触发点）；
    // null ⇒ 迁移期沿用 <c>NodeRelationGateway</c> 直调 + 可见化（退役条件：装配切片全量提供本入口后删回退支）。
    Operation.HierarchyMethod.Nodes.Linkage.INodeAttributionFlow? AttributionFlow = null,
    // 缺失处置姿态（可空 = AllowLegacyFallback，零行为变化）：缩减切片置 Reduced ⇒ 端口缺失静默（有意不参与，治误报）；
    // 收口装配置 RequireFlow ⇒ 缺失即诊断可见化。与 OneShotFlowFactory 切片口径同源（M2/M3 单一 owner）。
    Operation.HierarchyMethod.Nodes.Linkage.FlowPosture? Posture = null,
    // 诊断可见化端口（可空）：装配切片一处承载，供节点运行时各消费者取用（勿再各自开 log 形参）。
    IDiagnosticsSink? Diagnostics = null,
    // 收集端口「附加输入源」（可空；默认 null ⇒ 现状零回归）：由 Agent 组合根注入，仅在整包展开时追加
    // （P4-2 机制②：过程域 proc.* 节点并入 nodes.* 整包）。签名只用框架类型 ⇒ 框架零 Agent 依赖。
    Func<string, CancellationToken, Task<IReadOnlyList<CollectedNodePort>>>? AdditionalCollectSource = null);

/// <summary>
/// 节点运行时产物 —— 节点持久化/引用域的唯一组合点（单一装配入口组装，Y2 单例意图）。
/// <see cref="NodeOutputStore"/> / <see cref="NodeStepExecutionService"/> / <see cref="OutboxWriter{TWork}"/> 只在
/// <see cref="From"/> 内 new（消除多处重复 new 的构造细节散落）。各"壳"只消费本产物，不再各自开袋 + self-new 图。
/// </summary>
public sealed class NodeRoundRuntime
{
    public string WorkflowId { get; }
    /// <summary>节点输出存储（ur_nodes NodeType=output 背板）；NodeStore 未装配 → null（增强路径不阻断）。</summary>
    public INodeOutputStore? Outputs { get; }
    public NodeStepExecutionService Execution { get; }
    /// <summary>Outbox 后台写者（启用 Outbox 解耦时非空）；null = 原同步持久化（零回归）。</summary>
    public OutboxWriter<NodePersistWork>? Outbox { get; }

    private NodeRoundRuntime(
        string workflowId, INodeOutputStore? outputs, NodeStepExecutionService execution, OutboxWriter<NodePersistWork>? outbox)
    {
        WorkflowId = workflowId;
        Outputs = outputs;
        Execution = execution;
        Outbox = outbox;
    }

    /// <summary>唯一组合点：由装配切片构建节点运行时（NodeOutputStore / 执行服务 / OutboxWriter 单次组装）。
    /// Outbox 后台写者经 <see cref="NodeStepExecutionService.PersistAsync"/> 落库（热路径只 append，不落盘，对齐 R2 解耦）。
    /// 诊断端口经 <paramref name="diagnostics"/> 注入；缺省取 <see cref="NodePersistenceAssembly.Diagnostics"/>，
    /// 仍为空则落 <see cref="NullDiagnosticsSink"/>（禁裸 null 判定散落各消费者）。</summary>
    public static NodeRoundRuntime From(
        NodePersistenceAssembly assembly,
        string workflowId = NodeKeys.DefaultWorkflowId,
        IDiagnosticsSink? diagnostics = null)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        var sink = diagnostics ?? assembly.Diagnostics ?? NullDiagnosticsSink.Instance;
        var wf = NodeOutputKeys.Normalize(workflowId);
        var outputs = assembly.NodeStore != null ? new NodeOutputStore(assembly.NodeStore, wf) : null;
        var execution = new NodeStepExecutionService(
            outputs, wf, assembly.NodeStore, assembly.KeyMappings, assembly.BeginTransaction, assembly.Grounding,
            unifiedStateWrite: assembly.EnableUnifiedStateWrite,
            diagnostics: sink,
            attributionFlow: assembly.AttributionFlow,
            posture: assembly.Posture,
            // 附加输入源经装配切片透传（P4-2 机制②；默认 null ⇒ 零回归）。
            additionalCollectSource: assembly.AdditionalCollectSource);
        OutboxWriter<NodePersistWork>? outbox = null;
        if (assembly.EnableOutbox)
        {
            // R2 Outbox 泛型内核（Modules.Threading.Streaming.OutboxWriter<TWork>）：handler = PersistAsync；
            // 诊断端口在该组合点回填（含 node 键提取），框架内核零 Agent 依赖。
            outbox = new OutboxWriter<NodePersistWork>(
                (work, token) => execution.PersistAsync(work.Node, work.Props, work.Input, work.TaskKey, token),
                assembly.OutboxFlushTimeout ?? TimeSpan.FromSeconds(5),
                (kind, ctx) => sink.Report(OutboxLogKey(kind), new
                {
                    node = ctx.Item.Node.NodeKey,
                    elapsedMs = ctx.ElapsedMs,
                    timeoutMs = ctx.TimeoutMs,
                    errorType = ctx.ErrorType,
                    error = ctx.Error,
                    owner = ctx.Owner,
                    queue = ctx.Queue,
                }));
        }
        return new NodeRoundRuntime(wf, outputs, execution, outbox);
    }

    private static string OutboxLogKey(OutboxEventKind kind) => kind switch
    {
        OutboxEventKind.FlushSlow => "OutboxWriter_flush_slow",
        OutboxEventKind.ItemTimeout => "OutboxWriter_item_timeout",
        OutboxEventKind.ItemFailed => "OutboxWriter_item_failed",
        _ => "OutboxWriter_item_failed",
    };
}
