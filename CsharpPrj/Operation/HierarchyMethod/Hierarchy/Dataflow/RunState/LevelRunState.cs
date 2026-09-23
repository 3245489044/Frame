using System;
using System.Collections.Generic;
using Modules.DeclarativeDataflow.Model;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState;

/// <summary>
/// 层级实体的运行态（**通用模型，挂任意 <see cref="LevelRef"/>**）—— 把"谁在执行 / 处于什么阶段 / 进度多少"
/// 从"某层专属字段"提升为一等运行态维度，与层级轴正交。
/// <para>
/// 三轴正交（互不替代）：<see cref="Ref"/>（在哪一层哪个实体）× <see cref="Stage"/>（处于什么阶段，
/// 受控词表见 <see cref="RunStateStage"/>）× <see cref="Actor"/>（谁在执行，见 <see cref="ActorRef"/>）。
/// </para>
/// <para>
/// 关键性质：<see cref="Actor"/> 为 <c>null</c> = 该实体无执行体（如 history 容器 / 纯簿记节点），
/// 是**数据缺省**而非层级规则 —— 故"history 与子智能体无关、task 与子智能体强相关"由数据事实表达，
/// 不需要渲染层或投影层写专用分支。同理 <see cref="Origin"/> 显式给出事实来源，
/// 使"派生聚合"不会被误读为"该层真的在跑"。
/// </para>
/// <para>框架本体零域依赖：阶段/执行体均为受控文本，具体取值（cline/answer/…）由注入来源提供。</para>
/// </summary>
/// <param name="Ref">实体定位（层 + 实体 Id + 可选作用域）。</param>
/// <param name="Stage">受控阶段（<see cref="RunStateStage"/> 常量）。</param>
/// <param name="Actor">执行体（null = 无执行体；与阶段正交）。</param>
/// <param name="Round">当前轮次（null = 未运行/未知，不写 0 冒充）。</param>
/// <param name="StatusText">原始状态文本（保真：受控 stage 归一后仍可追溯来源词）。</param>
/// <param name="StartedAt">本轮起点（Live 来源提供）。</param>
/// <param name="ElapsedMs">已运行毫秒（Live 来源提供；终态由结算时长提供）。</param>
/// <param name="Metrics">度量（tokens/cost 等，键中立；无遥测为空）。</param>
/// <param name="Note">决策 / 诊断文本（通用，可为租约号、决策词等）。</param>
/// <param name="Origin">事实来源（优先级即合并序，见 <see cref="RunStateOrigin"/>）。</param>
public sealed record LevelRunState(
    LevelRef Ref,
    string Stage = RunStateStage.Idle,
    ActorRef? Actor = null,
    int? Round = null,
    string? StatusText = null,
    DateTimeOffset? StartedAt = null,
    long? ElapsedMs = null,
    IReadOnlyDictionary<string, double>? Metrics = null,
    string? Note = null,
    RunStateOrigin Origin = RunStateOrigin.Derived)
{
    /// <summary>是否进行中（<see cref="RunStateStage.IsActive"/>）。</summary>
    public bool IsActive => RunStateStage.IsActive(Stage);

    /// <summary>是否终态。</summary>
    public bool IsTerminal => RunStateStage.IsTerminal(Stage);

    /// <summary>是否携带有效执行体（<c>false</c> = 该实体无执行体）。</summary>
    public bool HasActor => Actor is { IsEmpty: false };

    /// <summary>执行体显示标签（无执行体 → 空串，模板可直接判空隐藏）。</summary>
    public string ActorLabel => Actor is { IsEmpty: false } a ? a.Label : string.Empty;

    /// <summary>质量/进度度量（无遥测为空字典，避免调用方判空）。</summary>
    public IReadOnlyDictionary<string, double> MetricsOrEmpty => Metrics ?? EmptyMetrics;

    private static readonly IReadOnlyDictionary<string, double> EmptyMetrics =
        new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>
    /// 字段级合并（**优先级合并的唯一实现**）：<paramref name="primary"/> 的非空字段覆盖
    /// <paramref name="fallback"/>；键（<see cref="Ref"/>）保持同一实体。
    /// <para>
    /// 设计理由：整条覆盖会丢信息（Live 源无轮次号而 Snapshot 源有），故按字段叠加；
    /// 一致性由"字段有值即权威"的单一规则保证，无优先级特例分支。
    /// </para>
    /// </summary>
    public static LevelRunState? Merge(LevelRunState? fallback, LevelRunState? primary)
    {
        if (primary is null) return fallback;
        if (fallback is null) return primary;
        if (fallback.Ref != primary.Ref) return primary; // 不同实体不合并（防御；调用方按 Ref 分组）

        return new LevelRunState(
            Ref: fallback.Ref,
            Stage: PickStage(fallback.Stage, primary.Stage),
            Actor: primary.HasActor ? primary.Actor : fallback.Actor,
            Round: primary.Round ?? fallback.Round,
            StatusText: FirstNonEmpty(primary.StatusText, fallback.StatusText),
            StartedAt: primary.StartedAt ?? fallback.StartedAt,
            ElapsedMs: primary.ElapsedMs ?? fallback.ElapsedMs,
            Metrics: primary.Metrics is { Count: > 0 } ? primary.Metrics : fallback.Metrics,
            Note: FirstNonEmpty(primary.Note, fallback.Note),
            Origin: primary.Origin >= fallback.Origin ? primary.Origin : fallback.Origin);
    }

    /// <summary>阶段合并：非空 / 非 <see cref="RunStateStage.Idle"/> 者优先；双 Idle 保持 Idle（不无证据升级）。</summary>
    private static string PickStage(string? fallbackStage, string? primaryStage)
    {
        var p = string.IsNullOrWhiteSpace(primaryStage) ? null : primaryStage!.Trim();
        var f = string.IsNullOrWhiteSpace(fallbackStage) ? null : fallbackStage!.Trim();
        if (p is null) return f ?? RunStateStage.Idle;
        if (f is null) return p;
        // 高优先级源给出 Idle（无在途事实）而低优先级源有事实 → 保留低优先级事实（不抹掉已落库状态）。
        return string.Equals(p, RunStateStage.Idle, StringComparison.OrdinalIgnoreCase) ? f : p;
    }

    private static string? FirstNonEmpty(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) ? a : (string.IsNullOrWhiteSpace(b) ? null : b);

    /// <summary>构造在途运行态（Live 来源：登记面即事实）。</summary>
    public static LevelRunState Live(
        LevelRef reference, ActorRef? actor, DateTimeOffset dispatchedAt, long elapsedMs,
        int? round = null, IReadOnlyDictionary<string, double>? metrics = null, string? note = null)
        => new(reference, RunStateStage.Running, actor, round, RunStateStage.Running, dispatchedAt, elapsedMs,
            metrics, note, RunStateOrigin.Live);

    /// <summary>构造快照运行态（Snapshot 来源：已落库事实；执行体类别由调用方声明，缺省按代理类）。</summary>
    public static LevelRunState Snapshot(
        LevelRef reference, string? statusText, int round, ActorRef? actor = null, string? note = null)
        => new(reference,
            RunStateStage.FromStatusText(statusText),
            actor,
            round > 0 ? round : null,
            statusText,
            Note: note,
            Origin: RunStateOrigin.Snapshot);

    /// <summary>构造派生运行态（Derived 来源：容器层汇总；**无执行体** —— 与子智能体解耦的事实表达）。</summary>
    public static LevelRunState Derived(LevelRef reference, string stage, string? note = null)
        => new(reference, RunStateStage.Normalize(stage), Actor: null, Note: note, Origin: RunStateOrigin.Derived);

    public override string ToString()
        => $"{Ref.Key} stage={Stage} actor={(HasActor ? ActorLabel : "-")} round={(Round?.ToString() ?? "-")} origin={Origin}";
}
