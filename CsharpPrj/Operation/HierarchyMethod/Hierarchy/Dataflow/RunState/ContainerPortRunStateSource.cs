using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.RunState;
using Operation.Container.Runtime;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 快照运行态来源（<see cref="RunStateOrigin.Snapshot"/>）—— 包装既有的单实体运行状态端口
/// <see cref="IContainerRuntimePort"/>（其实现读持久快照），把端口词汇中立 DTO 映射为通用
/// <see cref="LevelRunState"/>。
/// <para>
/// 端口语义 = "按实体键读运行状态"，与层无关（层由外部 schema 决定）；未装配端口 / 未命中 → null。
/// 执行体类别由 <see cref="ActorKind"/> 声明（缺省 <see cref="ActorKinds.Agent"/>）——
/// 这是**适配层的域映射决策**（该端口的执行器标识在本域内为代理类），框架本体不做此假设。
/// </para>
/// </summary>
public sealed class ContainerPortRunStateSource : IRunStateSource
{
    private readonly IContainerRuntimePort? _port;

    /// <param name="port">单实体运行状态端口（可空；未装配 → 恒 null 诊断透传）。</param>
    /// <param name="actorKind">执行器标识的类别（缺省代理类；调用方可注入其它受控词）。</param>
    public ContainerPortRunStateSource(IContainerRuntimePort? port, string? actorKind = null)
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
    public static LevelRunState FromStatus(LevelRef reference, ContainerTaskRunStatus status, string actorKind)
    {
        ArgumentNullException.ThrowIfNull(status);

        // E7 修复（对称）：阶段文本口径与 AgentHistoryRuntimePort 侧一致 —— 直接取任务池状态，
        // 不再对本来源单独施加「Idle 时改取 ExecutorStatus」的不对称启发式（消除「同一事实两路得不同 Stage」）。
        var actor = ActorRef.OrNull(actorKind, status.ExecutorId);
        // R1 修复：Snapshot 已放入 actor，原紧随的 `with { Actor = actor ?? state.Actor }` 为恒等赋值，删除。
        return LevelRunState.Snapshot(reference, status.Status, status.Round, actor, status.Decision);
    }
}
