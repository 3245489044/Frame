using System.Collections.Frozen;
using Modules.Node.Projection;

namespace Modules.Node.RunState;

/// <summary>运行态阶段受控词（唯一来源；与外部 RuntimeStage 经显式映射，禁散落字面量）。</summary>
public enum RunStateStage
{
    Idle,
    Started,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>执行体引用（Kind 为开放受控词：agent / tool / engine）。</summary>
public sealed record NodeActorRef(string Kind, string Id)
{
    public override string ToString() => $"{Kind}:{Id}";
}

/// <summary>
/// 节点运行态（G4 收编）：受控词 + 转移表 + 计数/时间戳 + 来源可辨。
/// <b>不持有业务状态框架实现</b>（具体状态机留宿主），也不写库。
/// </summary>
public sealed class NodeRunState
{
    /// <summary>
    /// 转移表单源：未在表中声明出边的阶段即终态（<see cref="IsTerminal"/> 由本表派生，禁两处各自列举）。
    /// 冻结字典（不暴露可变静态，K5）。
    /// </summary>
    private static readonly FrozenDictionary<RunStateStage, RunStateStage[]> TransitionTable = BuildTransitionTable();

    private static readonly FrozenDictionary<string, RunStateStage> ExternalStageMap =
        new Dictionary<string, RunStateStage>(StringComparer.OrdinalIgnoreCase)
        {
            ["pending"] = RunStateStage.Idle,
            ["queued"] = RunStateStage.Idle,
            ["idle"] = RunStateStage.Idle,
            ["starting"] = RunStateStage.Started,
            ["started"] = RunStateStage.Started,
            ["in_progress"] = RunStateStage.Running,
            ["inprogress"] = RunStateStage.Running,
            ["running"] = RunStateStage.Running,
            ["active"] = RunStateStage.Running,
            ["paused"] = RunStateStage.Paused,
            ["suspended"] = RunStateStage.Paused,
            ["done"] = RunStateStage.Completed,
            ["completed"] = RunStateStage.Completed,
            ["succeeded"] = RunStateStage.Completed,
            ["skipped"] = RunStateStage.Cancelled,
            ["failed"] = RunStateStage.Failed,
            ["error"] = RunStateStage.Failed,
            ["cancelled"] = RunStateStage.Cancelled,
            ["canceled"] = RunStateStage.Cancelled,
            ["aborted"] = RunStateStage.Cancelled,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static FrozenDictionary<RunStateStage, RunStateStage[]> BuildTransitionTable()
    {
        var table = new Dictionary<RunStateStage, RunStateStage[]>
        {
            [RunStateStage.Idle] = new[] { RunStateStage.Started, RunStateStage.Running, RunStateStage.Cancelled },
            [RunStateStage.Started] = new[] { RunStateStage.Running, RunStateStage.Paused, RunStateStage.Failed, RunStateStage.Cancelled },
            [RunStateStage.Running] = new[] { RunStateStage.Paused, RunStateStage.Completed, RunStateStage.Failed, RunStateStage.Cancelled },
            [RunStateStage.Paused] = new[] { RunStateStage.Running, RunStateStage.Completed, RunStateStage.Failed, RunStateStage.Cancelled },
        };

        foreach (var stage in Enum.GetValues<RunStateStage>())
        {
            // 未声明出边的阶段 = 终态（Completed / Failed / Cancelled 及未来新增阶段自动归位）
            table.TryAdd(stage, Array.Empty<RunStateStage>());
        }

        return table.ToFrozenDictionary();
    }

    public NodeRunState(
        RunStateStage stage = RunStateStage.Idle,
        NodeActorRef? actor = null,
        int? round = null,
        string? statusText = null,
        DateTimeOffset? startedAt = null,
        long? elapsedMs = null,
        ProjectionOrigin origin = ProjectionOrigin.Snapshot,
        string? note = null)
    {
        Stage = stage;
        Actor = actor;
        Round = round;
        StatusText = statusText;
        StartedAt = startedAt;
        ElapsedMs = elapsedMs;
        Origin = origin;
        Note = note;
    }

    public RunStateStage Stage { get; }

    /// <summary>Actor = ∅ 即该层级实体无执行体（容器层/纯簿记层），为数据缺省而非层级规则。</summary>
    public NodeActorRef? Actor { get; }

    public int? Round { get; }

    public string? StatusText { get; }

    public DateTimeOffset? StartedAt { get; }

    public long? ElapsedMs { get; }

    public ProjectionOrigin Origin { get; }

    public string? Note { get; }

    public bool HasActor => Actor is not null;

    /// <summary>终态派生自转移表（无出边即终态）：单源，禁两处各自列举。</summary>
    public bool IsTerminal => TransitionTable.TryGetValue(Stage, out var allowed) && allowed.Length == 0;

    public static IReadOnlyDictionary<string, RunStateStage> DefaultExternalStageMap => ExternalStageMap;

    public bool CanTransition(RunStateStage next) =>
        TransitionTable.TryGetValue(Stage, out var allowed) && allowed.Contains(next);

    /// <summary>合法转移则产出新快照（不可变）；非法转移返回 false（调用方可见化，不静默）。</summary>
    public bool TryTransition(RunStateStage next, out NodeRunState nextState)
    {
        if (!CanTransition(next))
        {
            nextState = this;
            return false;
        }

        nextState = new NodeRunState(
            next,
            Actor,
            Round,
            StatusText,
            StartedAt,
            ElapsedMs,
            Origin,
            Note);
        return true;
    }

    /// <summary>外部阶段词 → 受控词（声明覆盖优先，未命中 ⇒ null，<b>不伪造 Idle</b>）。</summary>
    public static RunStateStage? MapExternalStage(
        string? externalStage,
        IReadOnlyDictionary<string, RunStateStage>? overrides = null)
    {
        if (string.IsNullOrWhiteSpace(externalStage))
        {
            return null;
        }

        if (overrides is not null && overrides.TryGetValue(externalStage, out var overridden))
        {
            return overridden;
        }

        return ExternalStageMap.TryGetValue(externalStage, out var mapped) ? mapped : null;
    }

    /// <summary>外部运行态装配（无命中 ⇒ null，不伪造事实）。</summary>
    public static NodeRunState? FromExternalStage(
        string? externalStage,
        IReadOnlyDictionary<string, RunStateStage>? overrides = null,
        ProjectionOrigin origin = ProjectionOrigin.Snapshot)
    {
        var mapped = MapExternalStage(externalStage, overrides);
        return mapped is null ? null : new NodeRunState(mapped.Value, origin: origin);
    }

    /// <summary>展示投影：<c>Actor=∅ → 只渲染阶段/轮次/时长</c>；有执行体则追加角标 + 来源可辨。</summary>
    public string Describe()
    {
        var parts = new List<string> { Stage.ToString().ToLowerInvariant() };

        if (Round is { } round)
        {
            parts.Add($"r{round}");
        }

        if (ElapsedMs is { } elapsed)
        {
            parts.Add($"{elapsed / 1000.0:F1}s");
        }

        if (HasActor)
        {
            parts.Add($"[{Actor}]");
            parts.Add($"({Origin})");
        }

        if (!string.IsNullOrWhiteSpace(StatusText))
        {
            parts.Add(StatusText);
        }

        return string.Join(" · ", parts);
    }
}
