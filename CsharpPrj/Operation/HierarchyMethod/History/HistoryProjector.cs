using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Nodes.Workflows;
using Operation.HierarchyMethod.Tasks;
using Share.Conversation;
namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史投影器默认实现（History 层，LLM-free）：把一轮编排结果沉淀到历史容器并回喂为下轮上下文。
/// 写边原语统一委托 <see cref="IHistoryRelationManager"/>（history_owns_task / session_owns / round_sequence），
/// 本实现只编排不复制写边（SingleImplementation / TH-12）。装配可空：未装配关系管理器 → 沉淀/回读均 no-op，
/// 对齐「未装配返回诊断、不阻断 DB-only 路径」模式。Agent 可注入本实现或替换为 Conversation 历史适配器。
/// </summary>
public sealed class HistoryProjector : IHistoryProjector
{
    private readonly IHistoryRelationManager? _relations;
    private readonly INodeStore? _nodes;

    /// <param name="relations">历史关系管理端口（可空；未装配则沉淀/回读均跳过）。</param>
    /// <param name="nodes">节点库端口（可空；未装配则入史投影（L-B 节点）跳过，仅建关系边）。</param>
    public HistoryProjector(IHistoryRelationManager? relations = null, INodeStore? nodes = null)
    {
        _relations = relations;
        _nodes = nodes;
    }

    /// <summary>
    /// 沉淀一轮：幂等建历史容器 → 归拢任务（history_owns_task）→ 挂载节点（session_owns）→ 消息时序边（round_sequence）。
    /// 历史容器 Id 优先取 <see cref="TaskScope.HistoryId"/>，缺失回退 taskKey（task-key 作用域历史）。
    /// </summary>
    public async Task PersistRoundAsync(NodeRoundResult round, TaskScope scope, CancellationToken ct = default)
    {
        if (_relations is null || scope is null) return;

        var historyId = ResolveHistoryId(scope.HistoryId, scope.TaskKey);
        var taskKey = scope.TaskKey;
        await _relations.CreateAsync(historyId, ct: ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(taskKey))
            await _relations.AttachTaskAsync(historyId, taskKey, ct).ConfigureAwait(false);

        // 节点即结果：逐条以 task→node M:N 关联挂载（历史节点集派生 = 其任务节点并集，不再直写 history→node 边）。
        // 注：消息时序边 round_sequence 已退役（A4′）——历史顺序改由键内 (round, seq) 承载
        // （SessionMessageKeys.OrderComparer 读侧排序，见 RefHead.Project），本处不再写时序边。
        foreach (var rec in round?.Records ?? Array.Empty<NodeExecutionRecord>())
        {
            if (string.IsNullOrWhiteSpace(rec.NodeKey)) continue;
            // 命名空间取**存储命名空间**（与 ProjectMarkedAsync/落库侧同一权威）；不消费 rec.WorkflowId 以免来源名泄漏入图端点。
            var workflowId = NodeKeys.DefaultWorkflowId;
            if (!string.IsNullOrWhiteSpace(taskKey))
                await _relations.LinkNodeAsync(taskKey, workflowId, rec.NodeKey, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 从某 task 累积历史重建下一轮执行上下文（事务无关的只读重建）。本默认实现仅回喂已归拢任务键集；
    /// 完整历史投影（L0/L1/L2 提示词上下文）由 <see cref="IContextProjector"/> 层（阶段五）完成。
    /// </summary>
    /// <remarks>
    /// 统一 Id 规则（<see cref="ResolveHistoryId"/>）：无显式 HistoryId 时 historyId == taskKey。
    /// 若沉淀（<see cref="PersistRoundAsync"/>）使用了显式 <see cref="TaskScope.HistoryId"/>，
    /// 则回读须以同一容器键发起（本方法参数即容器键），否则历史回读与沉淀不对称。
    /// </remarks>
    public async Task<NodeRoundInput> BuildContextAsync(string taskKey, string? sessionId, CancellationToken ct = default)
    {
        var vars = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (_relations is not null && !string.IsNullOrWhiteSpace(taskKey))
        {
            // 以容器键（按统一规则 = taskKey）枚举归拢任务键（历史回读的轻量形态）。
            var historyId = ResolveHistoryId(null, taskKey);
            var keys = await _relations.GetTaskKeysAsync(historyId, ct).ConfigureAwait(false);
            if (keys is { Count: > 0 })
                vars["history_task_keys"] = keys;
        }

        return new NodeRoundInput(taskKey, sessionId, 0, vars);
    }

    /// <summary>统一历史容器 Id 解析（单一规则）：显式 historyId 优先，缺省回退 taskKey（historyId == taskKey 约定）。</summary>
    private static string ResolveHistoryId(string? historyId, string taskKey)
        => string.IsNullOrWhiteSpace(historyId) ? taskKey : historyId;

    /// <summary>
    /// 入史投影（CP-3 唯一入口）：按 <see cref="HistoryMarkPolicy"/> 过滤 → 标记节点依次投影为
    /// <c>NodeType=conversation_message</c> 节点（<c>session.{name}.r{round}.msg.{seq}</c>，键生成唯一权威 =
    /// <see cref="SessionMessageKeys"/>）+ <c>session_owns</c> 挂载边 + <c>round_sequence</c> 相邻时序边。
    /// <para>时序权威 = 键内 <c>(round, seq)</c>（读侧排序见 <see cref="SessionMessageKeys.Compare"/>）；
    /// <c>round_sequence</c> 为轮内相邻边（跨轮时序由键序表达），跨轮续接随「边单写」批收敛。</para>
    /// <para>未装配节点库 → 不写 L-B 节点（仅建边），返回 0；未装配关系管理 → 全跳过。</para>
    /// </summary>
    public async Task<int> ProjectMarkedAsync(
        RoundArtifacts artifacts, RoundSpec? spec, TaskScope scope, CancellationToken ct = default)
    {
        if (artifacts is null || scope is null || _relations is null) return 0;
        var taskKey = scope.TaskKey;
        if (string.IsNullOrWhiteSpace(taskKey)) return 0;

        // 历史容器 + 任务归拢（历史节点集经 task→node 派生，故容器本身可不出现于 L-B 键域）。
        var historyId = ResolveHistoryId(scope.HistoryId, taskKey);
        await _relations.CreateAsync(historyId, ct: ct).ConfigureAwait(false);
        await _relations.AttachTaskAsync(historyId, taskKey, ct).ConfigureAwait(false);

        var workflowId = NodeKeys.DefaultWorkflowId;
        // 轮次归一（键的身份组成，见 SessionMessageKeys）：0/负值会使「跨轮同名覆盖」回归，故下限取 1。
        var round = artifacts.Round > 0 ? artifacts.Round : 1;
        var seq = 0;
        foreach (var record in (artifacts.Nodes ?? Array.Empty<NodeExecutionRecord>()))
        {
            var mark = HistoryMarkPolicy.Resolve(spec, record, artifacts.TerminalNodeKey);
            if (mark == NodeHistoryMark.None) continue;

            seq++;
            var messageKey = SessionMessageKeys.Build(taskKey, round, seq);
            if (_nodes is not null)
                await _nodes.PutAsync(new NodeRecord
                {
                    NodeKey = messageKey,
                    NodeType = NodeTypes.ConversationMessage,
                    ThreadId = workflowId,
                    Status = mark.ToString(),
                    PayloadJson = JsonSerializer.Serialize(ToMessage(artifacts, record, mark), PayloadJsonOptions),
                }, ct).ConfigureAwait(false);

            // 挂载到会话根（session_owns）；轮内时序不再写边（A4′ 退役）—— 顺序由键内 (round, seq) 承载。
            await _relations.AddSessionMessageLinkAsync(taskKey, workflowId, messageKey, ct).ConfigureAwait(false);
        }

        return seq;
    }

    /// <summary>节点载荷 JSON 选项（不转义非 ASCII：载荷供任务面板展示/排障直读，中文明文更具可用性）。</summary>
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>标记节点 → 单条规范消息（Terminal = assistant 终答；Numbered = tool 分层气泡）。</summary>
    private static ConversationMessage ToMessage(RoundArtifacts artifacts, NodeExecutionRecord record, NodeHistoryMark mark)
    {
        var isTerminal = mark == NodeHistoryMark.Terminal;
        var text = isTerminal && !string.IsNullOrWhiteSpace(artifacts.TerminalText)
            ? artifacts.TerminalText!
            : FirstPortText(record.OutputPorts) ?? string.Empty;
        return new ConversationMessage(
            isTerminal ? ConversationRole.Assistant : ConversationRole.Tool,
            record.NodeKey,
            isTerminal ? null : record.NodeKey,
            text,
            artifacts.Round,
            record.AtUtc == default ? DateTimeOffset.UtcNow : record.AtUtc);
    }

    /// <summary>节点产出的首个非空白端口文本（无端口/全空 → null）。</summary>
    private static string? FirstPortText(IReadOnlyDictionary<string, object?>? ports)
    {
        if (ports is null || ports.Count == 0) return null;
        foreach (var value in ports.Values)
        {
            if (value is null) continue;
            var text = value as string ?? value.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }
}
