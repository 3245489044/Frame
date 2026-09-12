using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Threading.Streaming;
using Share.HierarchySupport;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 步骤后持久化横切坐标器（写入侧横切唯一点，SingleImplementation / TH-12）。
/// <para>
/// 自 Agent 侧 <c>RoundStepInvoker.OnAfterStepAsync</c> 下沉：把「暂存读取 → Outbox append（或同步 Persist）→
/// <see cref="NodeRoundContext.MarkPersistedPort"/> → DP-3 投影 → 失败归类不翻转」统一为框架横切，
/// 依赖全部注入（执行服务 / Outbox 写者 / 双写收敛开关 / 日志回调），Agent 侧只保留步骤语义。
/// </para>
/// <para>
/// 失败**单独归类 + 归因日志、不翻转业务 ok**（增强路径失败非致命）；取消上抛，交由调用方按取消语义处理。
/// </para>
/// </summary>
public sealed class NodeStepPersistCoordinator
{
    private readonly NodeStepExecutionService _service;
    private readonly OutboxWriter<NodePersistWork>? _outbox;
    private readonly bool _unifiedStateWrite;
    private readonly Action<string, object?>? _log;

    public NodeStepPersistCoordinator(
        NodeStepExecutionService service,
        OutboxWriter<NodePersistWork>? outbox = null,
        bool unifiedStateWrite = false,
        Action<string, object?>? log = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _outbox = outbox;
        _unifiedStateWrite = unifiedStateWrite;
        _log = log;
    }

    /// <summary>
    /// 步骤后横切持久化（INodeExecutorHooks.OnAfterStepAsync，编排器 onAfterStep 调用，D2 解耦）：
    /// 读取步骤体暂存到 <paramref name="ctx"/> 的节点/产物/入参溯源，落库 ur_nodes + 节点元数据（SQL KeyMapping + 图关系）。
    /// </summary>
    public async Task OnAfterStepAsync(NodeRoundContext ctx, NodeExecutionRecord record, CancellationToken ct)
    {
        if (!ctx.TryGetStep(record.NodeKey, out var node, out var props, out var input) || node is null || input is null)
            return; // 步骤未执行（如 CanExecute 跳过）→ 无产物可持久化

        // 入参快照去空值（落库形态）；溯源原样随行，供图 NodeToNode 组合边持久化。
        var resolution = new NodeInputSnapshot(
            input.Parameters.Where(kv => kv.Value != null)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            input.Provenance);

        // R2 Outbox 解耦（D2 根治）：启用时热路径只 append（内存 O(1)，不落盘），落库由后台 OutboxWriter 消费，
        // 任一存储卡顿不再冻结本轮 → 后续步骤在 guard_input 完成后立即开始（与存储健康无关）。
        if (_outbox is not null)
        {
            _outbox.Append(new NodePersistWork(node, props, resolution, ctx.SessionId));
            // 逻辑结果即时：已入队视为已持久化（存储最终一致），登记端口供终态信封补漏判定（避免补写双写）。
            foreach (var port in props.Keys)
                ctx.MarkPersistedPort(node.NodeKey, port);
            // DP-3（开关开）：权威落库在途 → 以同轮产出做在途投影（读侧桥接；权威仍是 ur_nodes，落库后结果一致）。
            if (_unifiedStateWrite) ProjectInFlight(node, props, ctx.Vars);
            return;
        }

        // 未启用 Outbox → 原同步持久化（零回归；落库失败单独归类、不翻转业务 ok）。
        try
        {
            await _service.PersistAsync(node, props, resolution, ctx.SessionId, ct).ConfigureAwait(false);
            // 横切持久化成功 → 登记本轮已落库端口，供终态信封补漏判定（已写则不再补写，消除 R1 双写）。
            // 失败/取消不登记：信封兜底补写，保持"guard_output 被 CanExecute 跳过仍落 safe_output"的既有语义。
            foreach (var port in props.Keys)
                ctx.MarkPersistedPort(node.NodeKey, port);
            // DP-3（开关开）：自权威投影重算回填数据空间（取代执行期扁平写入，写侧保持单写）。
            if (_unifiedStateWrite) await _service.ProjectOutputsAsync(node, ctx.Vars, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // 取消上抛，交由调用方按取消语义处理，不归为持久化失败
        }
        catch (Exception ex)
        {
            // D2：持久化失败单独归类（errorClass）+ 归因日志，不翻转业务 ok（对齐「增强路径失败非致命」原则）。
            _log?.Invoke("WorkflowRunner_stepPersist_failed",
                new { function = record.NodeKey, errorClass = ErrorClassifier.ClassifyError(ex).ToString(), error = ex.Message });
        }
    }

    /// <summary>在途投影（DP-3 开关开）：Outbox 后台落库在途时，以同轮产出值桥接数据空间读写侧；
    /// 权威仍是 ur_nodes（落库完成后 <see cref="NodeStepExecutionService.ProjectOutputsAsync"/> 重算同结果），本处只做读侧桥接。</summary>
    private static void ProjectInFlight(NodeDefinition node, System.Collections.Generic.IReadOnlyDictionary<string, object?> props, IDataSpace vars)
    {
        foreach (var key in node.ResultKeys)
            if (props.TryGetValue(key, out var v) && v is not null)
                vars.Set(key, v);
    }
}
