using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;
using Share.Conversation;
using Share.Conversation.Aggregates;
using Share.Conversation.Commands;
using Share.Conversation.Ids;
using Share.Conversation.Ports;

namespace Operation.HierarchyMethod.History.Commands;

/// <summary>
/// 历史命令唯一实现（C1/C2/C8 聚拢点）：
/// 创建历史根 = root 注册表 + graph mirror（可选 HistoryRoot session）；
/// 归拢 task = 只写 history_owns_task 边，不隐式建 history/task；禁止自环。
/// 除本类外，任何层不得直接调用 <see cref="IHistoryRelationManager.CreateAsync"/> /
/// <see cref="IHistoryRelationManager.AttachTaskAsync"/>。
/// </summary>
public sealed class HistoryCommandService : IHistoryCommandPort
{
    private readonly IHistoryRootStore _roots;
    private readonly IHistoryRelationManager _relations;
    private readonly ICreationGate _gate;
    private readonly ISessionStore? _sessions;
    private readonly ISessionTranscriptStore? _transcripts;
    private readonly ITaskQuery? _tasks;
    private readonly IDiagnosticsSink _diagnostics;

    public HistoryCommandService(
        IHistoryRootStore roots,
        IHistoryRelationManager relations,
        ICreationGate? gate = null,
        ISessionStore? sessions = null,
        ITaskQuery? tasks = null,
        IDiagnosticsSink? diagnostics = null,
        ISessionTranscriptStore? transcripts = null)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _relations = relations ?? throw new ArgumentNullException(nameof(relations));
        _gate = gate ?? new DefaultCreationGate();
        _sessions = sessions;
        _tasks = tasks;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
        _transcripts = transcripts;
    }

    public async Task<HistoryRoot> CreateAsync(CreateHistoryCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var decision = _gate.AuthorizeHistory(cmd.Intent, cmd.Actor);
        if (!decision.Allowed)
            throw new InvalidOperationException($"创建历史被拒绝：{decision.Reason}");

        var existing = await _roots.GetAsync(cmd.Key, ct).ConfigureAwait(false);
        var root = existing ?? HistoryRoot.Create(cmd.Key, cmd.Name, cmd.Kind, cmd.Actor);
        if (existing is null)
        {
            await _roots.CreateAsync(root, ct).ConfigureAwait(false);
            try
            {
                await _relations.CreateAsync(root.Key.Value, root.Name, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _diagnostics.Report("HistoryCommand_root_mirror_failed", new { history = root.Key.Value, error = ex.Message });
            }
        }

        if (cmd.CreateRootSession && _sessions is not null)
        {
            var sessionKey = SessionKey.Create(root.Key.Value);
            if (await _sessions.GetAsync(sessionKey, ct).ConfigureAwait(false) is null)
            {
                await _sessions.CreateAsync(
                    SessionRecord.Create(sessionKey, SessionOwner.OfHistory(root.Key), SessionRole.HistoryRoot, "history"),
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

    public async Task AttachTaskAsync(AttachTaskCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        // DUP-4：自环判据只有写边守卫一处（ReferenceGrounding → HistorySelfLoopException）；
        // 本层不重复判断，避免两份错误源/两份文案。
        var decision = _gate.AuthorizeTask(
            cmd.Intent, new[] { cmd.History }, SessionRole.TaskMember, null, null, null);
        if (!decision.Allowed)
            throw new InvalidOperationException($"归拢任务被拒绝：{decision.Reason}");

        if (!await _roots.ExistsAsync(cmd.History, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"历史根不存在：{cmd.History.Value}（禁止隐式建根）");

        if (_tasks is not null && !await _tasks.ExistsAsync(cmd.Task, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"任务不存在：{cmd.Task.Value}（禁止隐式建任务）");

        await _relations.AttachTaskAsync(cmd.History.Value, cmd.Task.Value, ct).ConfigureAwait(false);
    }

    public Task DetachTaskAsync(DetachTaskCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return _relations.DetachTaskAsync(cmd.History.Value, cmd.Task.Value, ct);
    }

    public async Task<bool> DeleteAsync(DeleteHistoryCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var decision = _gate.AuthorizeHistory(cmd.Intent, cmd.Actor);
        if (!decision.Allowed)
            throw new InvalidOperationException($"删除历史被拒绝：{decision.Reason}");

        if (!await _roots.ExistsAsync(cmd.Key, ct).ConfigureAwait(false))
        {
            _diagnostics.Report("HistoryCommand_delete_missing", new { history = cmd.Key.Value });
            return false;
        }

        // 1) 图锚点/边（history_owns_task 出边随节点删除级联）。
        await _relations.DeleteAsync(cmd.Key.Value, ct).ConfigureAwait(false);

        // 2) 聚合拥有的会话与日志（顺序：先日志后会话；task 会话不在此删除）。
        if (cmd.DeleteSessions && _sessions is not null)
        {
            var owned = await _sessions.ListByOwnerAsync(SessionOwner.OfHistory(cmd.Key), ct).ConfigureAwait(false);
            foreach (var session in owned)
            {
                if (_transcripts is not null)
                    await _transcripts.DeleteAsync(session.Key, ct).ConfigureAwait(false);
                await _sessions.DeleteAsync(session.Key, ct).ConfigureAwait(false);
            }
        }

        // 3) 根行最后删：确保中途失败时仍可从 root store 看到“待清理”实体。
        await _roots.DeleteAsync(cmd.Key, ct).ConfigureAwait(false);
        _diagnostics.Report("HistoryCommand_deleted", new { history = cmd.Key.Value });
        return true;
    }
}
