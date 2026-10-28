using System;
using Operation.HierarchyMethod.Nodes.Execution;

namespace Operation.HierarchyMethod.Nodes.Workflows;

/// <summary>
/// 一次性声明流装配切片（**缩减切片口径单源**）—— 簿记流一律用缩减切片（无写透 / 无键映射 / 无历史投影器 / 无 Outbox，
/// 且切片内 AttributionFlow=null 防递归）。本类只持有切片口径；执行器构造与规格解析由应用层完成。
/// </summary>
public static class OneShotFlowFactory
{
    /// <summary>缩减切片：由基切片派生（写开口径单源；本类之外禁再写 with 缩减）。</summary>
    public static NodePersistenceAssembly Reduced(NodePersistenceAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly with
        {
            NodeStore = null,
            KeyMappings = null,
            EnableOutbox = false,
            // 有意不参与归属边（防递归）：姿态 Reduced ⇒ 调用点直接短路（不写边、不误报「未装配」）。
            Posture = global::Operation.HierarchyMethod.Nodes.Linkage.FlowPosture.Reduced,
        };
    }

    /// <summary>
    /// 读投影切片（S4）—— 由基切片派生：**保留**节点输出存储与引用域（收集式输入端口需要 task 作用域枚举
    /// + ur_nodes 节点输出读），**清**写侧（<c>AttributionFlow</c> / Outbox）：
    /// 读投影流不得反写历史与归属边（方案 §8-4「让 context_project 反写会话历史容器」= 明确不做）。
    /// <para>为何不能直接复用完整切片：<c>WorkflowRunner.RunRoundAsync</c> 在编排器返回后会调
    /// 历史写入 ⇒ 完整切片会让一次"读"产生历史写入。</para>
    /// <para><c>KeyMappings</c> 亦清：读投影不过键映射（留则无害但非读路径所需）。</para>
    /// </summary>
    /// <param name="assembly">基切片（须含 <c>NodeStore</c> 与 <c>Grounding</c>）。</param>
    public static NodePersistenceAssembly ReadProjection(NodePersistenceAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly with
        {
            KeyMappings = null,
            EnableOutbox = false,
            // 读投影不反写历史与归属边：姿态 Reduced ⇒ 调用点直接短路（有意不参与，非误报）。
            Posture = global::Operation.HierarchyMethod.Nodes.Linkage.FlowPosture.Reduced,
        };
    }
}
