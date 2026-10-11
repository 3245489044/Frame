using System.Collections.Generic;
using Operation.HierarchyMethod.History.Conversation;

namespace Operation.HierarchyMethod.History.Ports;

/// <summary>
/// 会话条目读取端口（框架声明，宿主实现）—— 把宿主域的原始轮载荷映射为**中性条目**
/// （<see cref="ConversationItem"/>），供域无关切分引擎消费。
/// <para>存在理由：切分算法不感知任何宿主消息形态（如 Cline / 其他子智能体），宿主经本端口完成域形态转换。</para>
/// </summary>
public interface IConversationItemReader
{
    /// <summary>解析一轮原始载荷为中性条目（按源序；解析失败/空 ⇒ 空集，不抛）。</summary>
    /// <param name="payloadJson">该轮原始载荷全文（宿主域形态）。</param>
    /// <param name="round">轮次序号（从 1 起）。</param>
    IReadOnlyList<ConversationItem> Read(string? payloadJson, int round);
}
