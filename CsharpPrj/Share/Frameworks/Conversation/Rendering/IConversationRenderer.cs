using System.Collections.Generic;

namespace Share.Conversation.Rendering;

/// <summary>
/// 会话历史渲染端口（IoC 反转）：由调用方（宿主/UI 库）注入"如何把渲染项序列化为目标载体内容"。
/// 载体由实现决定（html / text / listbox 项…），本契约与具体 UI 框架解耦；
/// 未装配时由消费方回退默认实现（不阻断）。单一消息来源当前为会话历史，
/// 故采用"中立渲染项 + 单端口"而非泛型注册表（避免过度设计）。
/// </summary>
public interface IConversationRenderer
{
    /// <summary>渲染整段会话历史为调用方目标载体内容；空列表由实现输出空态文案。</summary>
    string Render(IReadOnlyList<ConversationRenderItem> items);
}
