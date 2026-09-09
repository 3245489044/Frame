namespace Share.HierarchySupport
{
    /// <summary>
    /// 数据作用域 —— 统一数据传递标准的「生命周期/可见性」维度。
    /// 与 <see cref="DataDirection"/>（流向）正交：Direction 描述数据怎么流，Scope 描述数据活在哪个层。
    /// 定死的是规则（数据只有这五类作用域）；不定死的是内容（每个具体数据项经注册表声明其作用域与类型）。
    /// </summary>
    public enum DataScope
    {
        /// <summary>根快照作用域：跨轮 / 跨会话持久（当前 AgentTaskItem.Data）。单一数据持有方。</summary>
        Root,

        /// <summary>单轮内临时作用域（当前 RoundPipelineContext.Vars / 骨架函数间传递）。轮末可 Harvest 到根。</summary>
        Round,

        /// <summary>只读有界切片作用域：自上而下下放给子执行智能体（当前 DerivedAgentLease）。仅读、不回写。</summary>
        Lease,

        /// <summary>会话级作用域：跨轮但随会话生命周期存续（如 cline session / 对话明细）。</summary>
        Session,

        /// <summary>瞬态作用域：不入快照、不持久化，仅进程内临时（遥测、中间计算）。</summary>
        Volatile,
    }
}
