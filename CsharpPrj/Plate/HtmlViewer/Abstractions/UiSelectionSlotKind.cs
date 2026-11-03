#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// 选择槽受控词（框架词表，唯一来源）：槽名只在 <see cref="UiSelectionSlots"/> 声明，
/// 消费方按枚举比较（禁各处手写 "task"/"history"/"none" 字面量）。
/// </summary>
public enum UiSelectionSlotKind
{
    /// <summary>该视图不承载选择槽。</summary>
    None = 0,

    /// <summary>会话锚槽。</summary>
    Session = 1,

    /// <summary>历史容器锚槽。</summary>
    History = 2,

    /// <summary>任务锚槽。</summary>
    Task = 3,
}

/// <summary>选择槽名（受控词字面量的唯一声明点）。</summary>
public static class UiSelectionSlots
{
    public const string Session = "session";
    public const string History = "history";
    public const string Task = "task";
    public const string None = "none";

    /// <summary>枚举 → 槽名（未声明 ⇒ <see cref="None"/>）。</summary>
    public static string Name(UiSelectionSlotKind kind)
        => kind switch
        {
            UiSelectionSlotKind.Session => Session,
            UiSelectionSlotKind.History => History,
            UiSelectionSlotKind.Task => Task,
            _ => None,
        };

    /// <summary>声明词 → 枚举（未知 ⇒ <see cref="UiSelectionSlotKind.None"/>；default-deny）。</summary>
    public static UiSelectionSlotKind Parse(string? token)
        => token?.Trim().ToLowerInvariant() switch
        {
            Session => UiSelectionSlotKind.Session,
            History => UiSelectionSlotKind.History,
            Task => UiSelectionSlotKind.Task,
            _ => UiSelectionSlotKind.None,
        };
}
