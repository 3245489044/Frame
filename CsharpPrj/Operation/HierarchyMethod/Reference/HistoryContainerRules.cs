using System;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 历史容器 Id 规则（R3）—— 声明面 `Config\workflows\task-provision.ushar` 的 `[Space] HistoryContainerRule`
/// 在 Frame 侧的**目录对账单源**（禁各处再写第二份字面量/隐式回退）。
/// <para>
/// 规则（按序解析，首个非空即取；与声明面词表逐字一致）：
/// ① <c>explicit_history_id</c> — 调用方显式给出的容器 Id；
/// ② <c>task_key</c>            — 缺省回退 = taskKey（「historyId == taskKey」统一 Id 规则）。
/// </para>
/// <para>
/// 本类消费方 = <see cref="NodeTaskHistoryService"/> / <see cref="History.HistoryProjector"/> /
/// <see cref="History.NodeMaterializerBackedHistoryProjector"/>（原各自私有 <c>ResolveHistoryId</c> 的重复实现收敛于此）。
/// </para>
/// </summary>
public static class HistoryContainerRules
{
    /// <summary>规则 ①：显式容器 Id 来源键（声明面 `[Space] HistoryContainerRule` 首项）。</summary>
    public const string ExplicitHistoryId = "explicit_history_id";

    /// <summary>规则 ②：缺省回退来源键 = taskKey（声明面首项之后）。</summary>
    public const string TaskKeyFallback = "task_key";

    /// <summary>规则词表（声明 ⇄ 代码双向对账口径；顺序即解析优先级）。</summary>
    public static readonly string[] RuleKeys = { ExplicitHistoryId, TaskKeyFallback };

    /// <summary>展示名缺省来源键（声明面 `[Space] HistoryNameRule`）：缺省 = 容器 Id。</summary>
    public const string NameFallbackFromHistoryId = "history_id";

    /// <summary>展示名词表（声明 ⇄ 代码双向对账口径）。</summary>
    public static readonly string[] NameRuleKeys = { NameFallbackFromHistoryId };

    /// <summary>
    /// 按声明面规则解析历史容器 Id：显式 historyId 优先，缺省回退 taskKey（**显式规则**，非隐式回退）。
    /// taskKey 亦空 ⇒ 返回空串（调用方按 no-op / 诊断处理，禁静默造键）。
    /// </summary>
    /// <param name="historyId">显式容器 Id（可空）。</param>
    /// <param name="taskKey">任务键（回退来源）。</param>
    public static string Resolve(string? historyId, string? taskKey)
        => !string.IsNullOrWhiteSpace(historyId) ? historyId!.Trim()
         : !string.IsNullOrWhiteSpace(taskKey) ? taskKey!.Trim()
         : string.Empty;

    /// <summary>
    /// 按声明面 `[Space] HistoryNameRule` 解析历史展示名：显式 name 优先，缺省 = 容器 Id。
    /// **单点**（ERR-9：消除「流内 + fn 内」双层三元兜底；消费方不得再写第二份缺省规则）。
    /// </summary>
    /// <param name="historyName">显式展示名（可空 / 空白 ⇒ 缺省）。</param>
    /// <param name="historyId">容器 Id（缺省来源）。</param>
    public static string ResolveName(string? historyName, string historyId)
        => string.IsNullOrWhiteSpace(historyName) ? historyId : historyName!.Trim();
}
