using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Identity;
using Operation.DataStoreDriven.Core;
using Operation.HierarchyMethod.Abstractions;
using Share.Foundations.Diagnostics;
using Share.Conversation;
using Share.Conversation.Aggregates;
using Share.Conversation.Commands;
using Share.Conversation.Ids;
using Share.Conversation.Ports;
using UniversalWorkflowFramework.Relations.Mounting;

namespace Operation.Container.Commands;

/// <summary>
/// 历史命令唯一实现（C1/C2/C8 聚拢点）：
/// 创建历史根 = root 注册表 + graph mirror（可选 ContainerRoot session）；
/// 归拢 task = 只写 history_owns_task 边，不隐式建 history/task；禁止自环。
/// 除本类外，任何层不得直接调用 <see cref="IContainerRelationManager.CreateAsync"/> /
/// <see cref="IContainerRelationManager.AttachTaskAsync"/>。
/// </summary>
public sealed class ContainerCommandService : IContainerCommandPort
{
    private readonly IConversationLogRootStore _roots;
    private readonly IContainerRelationManager _relations;
    private readonly ICreationGate _gate;
    private readonly ISessionStore? _sessions;
    private readonly ISessionTranscriptStore? _transcripts;
    private readonly IDiagnosticsSink _diagnostics;
    private readonly ICompositeWriteUnit? _writeUnit;
    private readonly Func<IMountPort?>? _mounts;

    public ContainerCommandService(
        IConversationLogRootStore roots,
        IContainerRelationManager relations,
        ICreationGate? gate = null,
        ISessionStore? sessions = null,
        IDiagnosticsSink? diagnostics = null,
        ISessionTranscriptStore? transcripts = null,
        ICompositeWriteUnit? writeUnit = null,
        Func<IMountPort?>? mounts = null)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _relations = relations ?? throw new ArgumentNullException(nameof(relations));
        _gate = gate ?? new DefaultCreationGate();
        _sessions = sessions;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
        _transcripts = transcripts;
        _writeUnit = writeUnit;
        _mounts = mounts;
    }

    public async Task<ContainerRoot> CreateAsync(CreateContainerCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var decision = _gate.AuthorizeHistory(cmd.Intent, cmd.Actor);
        if (!decision.Allowed)
            throw new InvalidOperationException($"创建历史被拒绝：{decision.Reason}");

        var key = Normalize(cmd.Key);
        var existing = await _roots.GetAsync(key, ct).ConfigureAwait(false);
        var root = existing ?? ContainerRoot.Create(key, cmd.Name, cmd.Kind, cmd.Actor);
        if (existing is null)
        {
            if (_writeUnit is null)
            {
                // 迁移期直调路径（未装配写单元）：保留一版，装配写单元后不再进入（禁长期并行）。
                await _roots.CreateAsync(root, ct).ConfigureAwait(false);
                await _relations.CreateAsync(root.Key.Value, root.Name, ct).ConfigureAwait(false);
            }
            else
            {
                // 跨库写单元：SQL（根行）→ Graph（history 锚点）；图失败入 Outbox（不吞、不阻断 SQL 语义）。
                var result = await _writeUnit.ExecuteAsync(new StorageWriteUnit(
                    "history.create",
                    $"history:{root.Key.Value}:create:v1",
                    Sql: sqlCt => _roots.CreateAsync(root, sqlCt),
                    Graph: graphCt => _relations.CreateAsync(root.Key.Value, root.Name, graphCt),
                    PayloadJson: root.Key.Value), ct).ConfigureAwait(false);

                if (!result.SqlCommitted)
                    throw new InvalidOperationException(
                        $"创建历史根失败：{string.Join("; ", result.Reasons)}");

                if (!result.GraphApplied)
                    _diagnostics.Report("HistoryCommand_root_mirror_pending", new
                    {
                        history = root.Key.Value,
                        outbox = result.OutboxKey,
                        reasons = result.Reasons,
                    });
            }
        }

        if (cmd.CreateRootSession && _sessions is not null)
        {
            var sessionKey = SessionKey.Create(root.Key.Value);
            if (await _sessions.GetAsync(sessionKey, ct).ConfigureAwait(false) is null)
            {
                await _sessions.CreateAsync(
                    SessionRecord.Create(sessionKey, SessionOwner.OfHistory(root.Key), SessionRole.ContainerRoot, "history"),
                    ct).ConfigureAwait(false);
            }

            // 聚合一致性：root 会话必须有一条（可为空的）日志，history.get/context.get 才能“存在即命中”。
            if (_transcripts is not null
                && await _transcripts.GetAsync(sessionKey, ct).ConfigureAwait(false) is null)
            {
                await _transcripts.AppendProjectionAsync(sessionKey, new ConversationLog(), 0, ct).ConfigureAwait(false);
            }
        }

        return root;
    }

    /// <summary>D-01=A：命令面入参统一归一到 ContainerKey 规范面（裸身份），存储/图端点各自在其边界转换。</summary>
    private static ContainerKey Normalize(ContainerKey key)
    {
        if (key.IsEmpty) return key;
        var identity = NodeEndpointIds.NormalizeHistoryId(key.Value);
        return identity.Length == 0 ? key : ContainerKey.Create(identity);
    }

    public async Task AttachTaskAsync(AttachTaskCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var historyKey = Normalize(cmd.History);
        // DUP-4：自环判据只有写边守卫一处（ReferenceGrounding → ContainerSelfLoopException）；
        // 本层不重复判断，避免两份错误源/两份文案。
        var decision = _gate.AuthorizeTask(
            cmd.Intent, new[] { historyKey }, SessionRole.TaskMember, null, null, null);
        if (!decision.Allowed)
            throw new InvalidOperationException($"归拢任务被拒绝：{decision.Reason}");

        // 存在性判据唯一归属 = 关系内核写前门（EntityExistenceRegistry → 各实体 Store）：
        // 本层不再重复检查 root/task，避免「命令层通过、内核层拒绝」的两套判定（GP-2/E1）。
        // UMF · M3：归属边只经 IMountPort（命令端口保留 gate/root 语义；未装配门面 ⇒ 回退旧路径并可见化）。
        var mounts = _mounts?.Invoke();
        if (mounts is not null)
        {
            var receipt = await mounts.ApplyAsync(new MountRequest(
                MountIds.TaskToHistory,
                new MountScope("history", historyKey.Value,
                    CorrelationId: $"container-command:{historyKey.Value}:{cmd.Task.Value}"),
                new[] { new MountTarget("task", cmd.Task.Value) },
                MountIntent.Ensure,
                MountOrigin.Command,
                MountTriggers.CommandExplicit,
                Actor: cmd.Actor,
                CorrelationId: $"container-command:{historyKey.Value}:{cmd.Task.Value}"), ct).ConfigureAwait(false);

            if (receipt.Ok) return;

            throw new InvalidOperationException(
                $"归拢任务失败（mount={MountIds.TaskToHistory}, outcome={receipt.Outcome}）：{receipt.Reason}");
        }

        await _relations.AttachTaskAsync(historyKey.Value, cmd.Task.Value, ct).ConfigureAwait(false);
    }

    public async Task DetachTaskAsync(DetachTaskCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var historyKey = Normalize(cmd.History);

        var mounts = _mounts?.Invoke();
        if (mounts is not null)
        {
            var receipt = await mounts.ApplyAsync(new MountRequest(
                MountIds.TaskToHistory,
                new MountScope("history", historyKey.Value,
                    CorrelationId: $"container-command:{historyKey.Value}:{cmd.Task.Value}"),
                new[] { new MountTarget("task", cmd.Task.Value) },
                MountIntent.Remove,
                MountOrigin.Command,
                MountTriggers.CommandExplicit,
                Actor: cmd.Actor,
                CorrelationId: $"container-command:{historyKey.Value}:{cmd.Task.Value}"), ct).ConfigureAwait(false);

            if (receipt.Ok) return;

            throw new InvalidOperationException(
                $"解除归拢失败（mount={MountIds.TaskToHistory}, outcome={receipt.Outcome}）：{receipt.Reason}");
        }

        await _relations.DetachTaskAsync(historyKey.Value, cmd.Task.Value, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(DeleteContainerCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var decision = _gate.AuthorizeHistory(cmd.Intent, cmd.Actor);
        if (!decision.Allowed)
            throw new InvalidOperationException($"删除历史被拒绝：{decision.Reason}");

        var key = Normalize(cmd.Key);
        if (!await _roots.ExistsAsync(key, ct).ConfigureAwait(false))
        {
            _diagnostics.Report("HistoryCommand_delete_missing", new { history = key.Value });
            return false;
        }

        // 1) 图锚点/边（history_owns_task 出边随节点删除级联）。
        await _relations.DeleteAsync(key.Value, ct).ConfigureAwait(false);

        // 2) 聚合拥有的会话与日志（顺序：先日志后会话；task 会话不在此删除）。
        if (cmd.DeleteSessions && _sessions is not null)
        {
            var owned = await _sessions.ListByOwnerAsync(SessionOwner.OfHistory(key), ct).ConfigureAwait(false);
            foreach (var session in owned)
            {
                if (_transcripts is not null)
                    await _transcripts.DeleteAsync(session.Key, ct).ConfigureAwait(false);
                await _sessions.DeleteAsync(session.Key, ct).ConfigureAwait(false);
            }
        }

        // 3) 根行最后删：确保中途失败时仍可从 root store 看到“待清理”实体。
        await _roots.DeleteAsync(key, ct).ConfigureAwait(false);
        _diagnostics.Report("HistoryCommand_deleted", new { history = key.Value });
        return true;
    }
}
