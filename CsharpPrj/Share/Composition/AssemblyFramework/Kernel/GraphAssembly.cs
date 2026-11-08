using System.Collections.Generic;

namespace Share.Composition;

/// <summary>
/// 图组装槽位名 —— 「创建任务 + 关联节点 + 引用边 + 入队」四类部件（设计文档 §5「节点」行）。
/// 与文本槽（L0/Skill/L1/L2）共用同一内核部件模型，但**不共用拼接语义**
/// （图组装的幂等 / 部分成功 / 诊断收集保留在产物与快照诊断字段，不入文本拼接）。
/// </summary>
public static class GraphSlots
{
    /// <summary>任务节点槽（写 ur_tasks 即激活 AI 循环）。</summary>
    public const string Task = "task";

    /// <summary>关联节点槽。</summary>
    public const string Node = "node";

    /// <summary>引用边槽（src -[refType]-> tgt）。</summary>
    public const string Edge = "edge";

    /// <summary>入队槽（激活消费）。</summary>
    public const string Enqueue = "enqueue";
}

/// <summary>
/// 图组装产物 —— 确定性图组装的输出。
/// <para>
/// 语义保留（与 Frame 侧确定性写图服务一致，本类型只做无依赖搬运）：
/// <b>幂等</b>（已存在节点跳过，不计入 <see cref="CreatedArtifacts"/>）、
/// <b>部分成功不回滚</b>（失败逐条记入 <see cref="Diagnostics"/>，已完成节点保留）、
/// <b>未装配降级</b>（端口缺失记诊断而非抛错，<see cref="Enqueued"/> = -1）。
/// </para>
/// </summary>
/// <param name="TaskKey">任务键。</param>
/// <param name="Enqueued">入队行号；-1 = 未入队（任务端口未装配）。</param>
/// <param name="CreatedArtifacts">本次真实新建的产物 Id（幂等已存在者不计入）。</param>
/// <param name="Diagnostics">逐条诊断（部分成功留痕；空 = 全量成功）。</param>
public sealed record GraphAssemblyOutcome(
    string TaskKey,
    long Enqueued,
    IReadOnlyList<string> CreatedArtifacts,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>部分成功：存在诊断但已完成节点保留（调用方不可假定整体原子）。</summary>
    public bool PartiallySucceeded => Diagnostics.Count > 0;

    /// <summary>是否已激活消费（入队成功）。</summary>
    public bool Activated => Enqueued >= 0;
}
