#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>渲染节点角色受控词（载体无关；禁出现任何载体名）。</summary>
public static class SurfaceRoleIds
{
    public const string Bubble = "bubble";
    public const string Attach = "attach";
    public const string Group = "group";
    public const string FieldRow = "fieldRow";
    public const string Badge = "badge";
    public const string Region = "region";

    /// <summary>折叠容器（唯一折叠语义角色；载体内部决定 details/FoldPanel/ASCII 前缀）。</summary>
    public const string Fold = "fold";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Bubble, Attach, Group, FieldRow, Badge, Region, Fold,
    };

    /// <summary>是否已注册角色（未注册 ⇒ <see cref="Unknown"/>，可见占位）。</summary>
    public static bool IsKnown(string? role) => role is not null && Known.Contains(role);

    /// <summary>规范化角色（未注册 ⇒ unknown）。</summary>
    public static string Normalize(string? role) => IsKnown(role) ? role! : Unknown;

    public const string Unknown = "unknown";
}

/// <summary>渲染单元受控词（field 的呈现形态；载体无关）。</summary>
public static class SurfaceCellIds
{
    public const string Text = "text";
    public const string Tree = "tree";
    public const string Kv = "kv";
    public const string Money = "money";
    public const string Duration = "duration";
    public const string Ref = "ref";
    public const string Code = "code";
    public const string Diff = "diff";
    public const string Table = "table";
    public const string Badge = "badge";
    public const string Unknown = "unknown";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Text, Tree, Kv, Money, Duration, Ref, Code, Diff, Table, Badge, Unknown,
    };

    /// <summary>是否已注册单元。</summary>
    public static bool IsKnown(string? cell) => cell is not null && Known.Contains(cell);

    /// <summary>规范化单元（未注册 ⇒ unknown ⇒ 可见占位）。</summary>
    public static string Normalize(string? cell) => IsKnown(cell) ? cell! : Unknown;
}

/// <summary>节点折叠/选中态（声明与运行态共用；不参与键）。</summary>
public enum UiNodeState
{
    Open = 0,
    Closed = 1,
    Selected = 2,
    Disabled = 3,
}
