#nullable enable

namespace Share.Conversation.Presentation;

/// <summary>呈现身份的**事实来源**（可审计：区分写侧事实、声明投影与旧数据兜底）。</summary>
public enum PresentationOrigin
{
    /// <summary>无来源（未知类）。</summary>
    Unknown = 0,

    /// <summary>写侧事实（终答 / 通道）。</summary>
    Fact,

    /// <summary>声明投影（帧字段 <c>presentation</c> 或塑形声明）。</summary>
    Declared,

    /// <summary>旧数据兜底（角色派生；仅限无事实/无声明时）。</summary>
    Legacy,
}
