namespace Strategy.Observability.Boundary;

/// <summary>
/// 边界种类（E9）：端口 / 数据 / 事件三类边界共用同一键命名空间，
/// 故「同一键不得既是端口又是数据」由注册表天然保证。
/// </summary>
public enum BoundaryKind
{
    /// <summary>可调用边界：输入契约 + 输出契约 + 依赖端口清单。</summary>
    Port,

    /// <summary>跨边界数据项：值类型 + 方向 + 作用域。</summary>
    Data,

    /// <summary>观测事件分类：载荷字段 + 度量键 + 枚举域。</summary>
    Event,
}

/// <summary>
/// 边界数据流向（E9）。与 <see cref="Share.HierarchySupport.DataDirection"/>（轮内 Seed/Harvest 语义）
/// 正交：本枚举描述「边界进出口」，后者描述「轮内数据流」。
/// </summary>
public enum BoundaryDirection
{
    /// <summary>入向：外部 → 边界内。</summary>
    In,

    /// <summary>出向：边界内 → 外部。</summary>
    Out,

    /// <summary>双向（端口输入输出同时成立）。</summary>
    Both,
}
