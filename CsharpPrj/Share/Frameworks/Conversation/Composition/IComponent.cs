namespace Share.Composition;

/// <summary>
/// 部件 —— 一份可注入的原子内容（纯数据、可序列化）。
/// 组装内核的一等输入单位：携带「落哪个槽 / 槽内顺序 / 内容本体」，不含任何组装策略。
/// 内容可为文本（提示词段）、节点、引用边或任意序列片段，由槽位装配器解释。
/// </summary>
public interface IComponent
{
    /// <summary>槽位标识（如 L0/Skill/L1/L2、task/edge、{history}/{taskList}）。</summary>
    string Slot { get; }

    /// <summary>槽内顺序（同槽内升序装配）。</summary>
    int Order { get; }

    /// <summary>内容本体（文本 / 节点 / 边 / 序列片段）。</summary>
    object? Content { get; }
}
