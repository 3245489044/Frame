namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>nodes 层命名空间占位（框架骨架阶段）。节点存储/输出/关系编排类型将落位于此。</summary>
    internal static class NodesNamespaceMarker { }
}

namespace Operation.HierarchyMethod.Tasks
{
    /// <summary>task 层命名空间占位（框架骨架阶段）。纯簿记/执行节点/循环快照根/注入端口将落位于此。</summary>
    internal static class TasksNamespaceMarker { }
}

namespace Operation.HierarchyMethod.History
{
    /// <summary>history 层命名空间占位（框架骨架阶段）。历史容器/消息日志/关系编排类型将落位于此。</summary>
    internal static class HistoryNamespaceMarker { }
}

namespace Operation.HierarchyMethod.History.Runtime
{
    /// <summary>history 会话编排层命名空间占位（运行态聚合 / 批量控制 / 装配声明，LLM-free 通用逻辑）。</summary>
    internal static class HistoryRuntimeNamespaceMarker { }
}

namespace Operation.HierarchyMethod.Hierarchy
{
    /// <summary>三层层级统一管理门面命名空间占位（多 nodes ↔ 多 tasks ↔ 单 history）。
    /// IHierarchyManager / HierarchyManager 组合根：编排引用管理（IHierarchyReferenceManager）与三层实例，零 LiteDB 依赖。</summary>
    internal static class HierarchyNamespaceMarker { }
}

namespace Operation.HierarchyMethod.Abstractions
{
    /// <summary>框架共享抽象命名空间占位（骨架阶段）。可空守卫诊断、跨层端口将落位于此。</summary>
    internal static class AbstractionsNamespaceMarker { }
}
