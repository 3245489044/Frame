using System;

namespace Operation.HierarchyMethod.Nodes.Keys;

/// <summary>
/// 键与坐标寻址单一模块（ERR-1 / ERR-3 根治）—— 节点的**命名空间**与**容器 Id** 的
/// 构造、归一、缺省解析只允许从本模块取；散落在各调用点的字面量与隐式缺省一律视为违规
/// （门禁 E15 从「数取值点」升级为「只允许本模块取值」）。
/// <para>
/// 设计要点：<c>NodeOutputKeys.Normalize(null)</c> 这类**隐式缺省**提升为显式命名常量
/// （<see cref="DefaultNamespace"/>）—— 调用点写常量即「我明确要用默认命名空间」，
/// 与「我没传 / 传了空」语义分离，前者可审计、可对账、可替换。
/// </para>
/// <para>
/// 消费口径：业务调用点只经本模块取坐标；归一/缺省规则的**实现**仍单源在
/// <see cref="NodeOutputKeys.Normalize"/> 与 <see cref="Reference.HistoryContainerRules"/>（声明面对账），
/// 本模块是其唯一对外门面（禁绕过直取）。
/// </para>
/// </summary>
public static class KeyAddressing
{
    /// <summary>默认命名空间（节点 ThreadId / workflowId；唯一权威 = <see cref="NodeKeys.DefaultWorkflowId"/>）。</summary>
    public const string DefaultNamespace = NamespaceRule.Default;

    /// <summary>命名空间归一（空 / null ⇒ <see cref="DefaultNamespace"/>）。</summary>
    /// <param name="workflowId">候选命名空间（可空）。</param>
    public static string NormalizeNamespace(string? workflowId) => NodeOutputKeys.Normalize(workflowId);

    /// <summary>
    /// 历史容器 Id 缺省解析（ERR-1 收口）：显式 Id 优先，缺省回退 <paramref name="fallback"/>。
    /// 规则实现单源 = <see cref="Reference.HistoryContainerRules"/>（声明面 `[Space] HistoryContainerRule` 对账单源）。
    /// </summary>
    /// <param name="explicitId">调用方显式给出的容器 Id（可空）。</param>
    /// <param name="fallback">缺省回退来源（通常 = taskKey）。</param>
    public static string ContainerId(string? explicitId, string? fallback)
        => Reference.HistoryContainerRules.Resolve(explicitId, fallback);

    /// <summary>
    /// 历史容器展示名缺省解析：显式名优先，缺省 = 容器 Id
    /// （规则实现单源 = <see cref="Reference.HistoryContainerRules.ResolveName"/>；禁各层再写三元兜底）。
    /// </summary>
    /// <param name="explicitName">调用方显式给出的展示名（可空 / 空白 ⇒ 缺省）。</param>
    /// <param name="containerId">容器 Id（缺省来源）。</param>
    public static string ContainerName(string? explicitName, string containerId)
        => Reference.HistoryContainerRules.ResolveName(explicitName, containerId);
}

/// <summary>
/// 命名空间规则常量（显式命名缺省）—— 消除 <c>Normalize(null)</c> 形态的隐式缺省调用点。
/// </summary>
public static class NamespaceRule
{
    /// <summary>默认命名空间（= <see cref="NodeKeys.DefaultWorkflowId"/>，ur_nodes 跨组件单一语义）。</summary>
    public const string Default = NodeKeys.DefaultWorkflowId;
}
