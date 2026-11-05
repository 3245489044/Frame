namespace Modules.Node.Gather;

/// <summary>
/// 塑形**可呈现最小字段**契约（FW-4 / I6）：合格 ⇔ <c>Title≠∅ ∨ Summary≠∅ ∨ Key≠∅</c>。
/// <para>
/// 不合格不得当作「成功塑形」静默产出（那正是「有行但视觉上等于空」的成因）；
/// 调用方须移出行集并以 <c>unshaped</c> 计数可见化。
/// </para>
/// </summary>
public static class ShapingWellFormed
{
    /// <summary>
    /// 判断塑形结果是否可呈现：**内容可见** ⇔ <c>Title≠∅ ∨ Summary≠∅</c>。
    /// <para>
    /// <c>Key</c> 是身份不是内容（恒非空），不计入合格性——否则判据恒真（U5 就测不出「有行但不可见」）。
    /// </para>
    /// </summary>
    public static bool IsSatisfied(DeclaredNodeShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return !string.IsNullOrWhiteSpace(shape.Title)
               || !string.IsNullOrWhiteSpace(shape.Summary);
    }
}
