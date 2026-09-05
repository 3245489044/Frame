namespace Share.HierarchySupport
{
    /// <summary>
    /// 数据契约方向 —— 统一数据传递标准的「数据流向」分类。
    /// 定死的是规则（数据只有这四类流向）；不定死的是内容（每个具体数据项经注册表声明其方向与类型）。
    /// </summary>
    public enum DataDirection
    {
        /// <summary>自上而下：调度侧（根快照）注入轮内总线（租约切片 / PassThrough 视图）。</summary>
        Seed,

        /// <summary>自下而上：轮内产物回写根快照（跨轮持久化）。</summary>
        Harvest,

        /// <summary>透传：Seed → 步骤 → Harvest 全程，由骨架函数消费。</summary>
        PassThrough,

        /// <summary>横切：token/budget/strategy 等跨轮次 / 跨阶段数据，独立作用域，与轮内数据分离。</summary>
        CrossCutting,
    }
}
