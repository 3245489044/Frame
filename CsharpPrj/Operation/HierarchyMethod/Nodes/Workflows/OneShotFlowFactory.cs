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
            HistoryProjector = null,
            EnableOutbox = false,
            AttributionFlow = null,
        };
    }
}
