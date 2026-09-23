using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Operation.HierarchyMethod.History.Runtime;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 快照运行态来源（<see cref="RunStateOrigin.Snapshot"/>）—— 包装既有的单实体运行状态端口
/// <see cref="IHistoryRuntimePort"/>（其实现读持久快照），把端口词汇中立 DTO 映射为通用
/// <see cref="LevelRunState"/>。
/// <para>
/// 端口语义 = "按实体键读运行状态"，与层无关（层由外部 schema 决定）；未装配端口 / 未命中 → null。
/// 执行体类别由 <see cref="ActorKind"/> 声明（缺省 <see cref="ActorKinds.Agent"/>）——
/// 这是**适配层的域映射决策**（该端口的执行器标识在本域内为代理类），框架本体不做此假设。
/// </para>
/// </summary>
public sealed class HistoryPortRunStateSource : IRunStateSource
{
    private readonly IHistoryRuntimePort? _port;

    /// <param name="port">单实体运行状态端口（可空；未装配 → 恒 null 诊断透传）。</param>
    /// <param name="actorKind">执行器标识的类别（缺省代理类；调用方可注入其它受控词）。</param>
    public HistoryPortRunStateSource(IHistoryRuntimePort? port, string? actorKind = null)
    {
        _port = port;
        ActorKind = string.IsNullOrWhiteSpace(actorKind) ? ActorKinds.Agent : actorKind!;
    }

    /// <summary>本来源声明的执行体类别。</summary>
    public string ActorKind { get; }

    /// <inheritdoc />
    public RunStateOrigin Origin => RunStateOrigin.Snapshot;

    /// <inheritdoc />
    public string SourceId => "history-runtime-port";

    /// <inheritdoc />
    public async Task<LevelRunState?> GetAsync(LevelRef reference, CancellationToken ct = default)
    {
        if (_port is null || reference.IsEmpty) return null;

        var status = await _port.GetStatusAsync(reference.Id, ct).ConfigureAwait(false);
        return status is null ? null : FromStatus(reference, status, ActorKind);
    }

    /// <summary>端口 DTO → 通用运行态（**唯一映射点**，禁各处重复映射）。</summary>
    /// <param name="reference">实体定位。</param>
    /// <param name="status">端口状态快照。</param>
    /// <param name="actorKind">执行体类别（受控词）。</param>
    public static LevelRunState FromStatus(LevelRef reference, HistoryTaskRunStatus status, string actorKind)
    {
        ArgumentNullException.ThrowIfNull(status);

        // 阶段：以任务池状态文本为主；其为未运行占位而执行器状态有值时，取执行器状态（更具体的执行阶段）。
        var stageText = status.Status;
        if (RunStateStage.FromStatusText(stageText) == RunStateStage.Idle
            && !string.IsNullOrWhiteSpace(status.ExecutorStatus))
            stageText = status.ExecutorStatus;

        var actor = ActorRef.OrNull(actorKind, status.ExecutorId);
        var state = LevelRunState.Snapshot(reference, stageText, status.Round, actor, status.Decision);
        return state with { Actor = actor ?? state.Actor };
    }
}
