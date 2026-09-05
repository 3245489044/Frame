namespace Operation.LoopFoldMethod.Node.Model;

/// <summary>
/// 编排槽位 —— 节点在消费形态中的摆放区段（头/中/尾），由槽位策略决定。
/// 语义（通用，不绑定提示词）：Head 为稳定前缀、Tail 为多变后缀、Body 为中间动态区，
/// 使「稳定前置、多变后置」的缓存友好排布可复用于任意消费域。
/// </summary>
public enum NodeSlot
{
    /// <summary>头部段（稳定前缀优先区）。</summary>
    Head,

    /// <summary>中部段（动态内容区）。</summary>
    Body,

    /// <summary>尾部段（多变后缀区）。</summary>
    Tail,
}
