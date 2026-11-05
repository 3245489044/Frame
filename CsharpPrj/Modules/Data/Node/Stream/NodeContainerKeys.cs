using System;

namespace Modules.Node.Stream;

/// <summary>
/// 节点**容器键**（分区/身份键首段）规范化单源（通用；中性）—— 只做首尾空白归一，不改大小写、不加/去前缀。
/// <para>大小写差异由 <see cref="Equivalent"/> 承担（比较面），不写回键本体。</para>
/// </summary>
public static class NodeContainerKeys
{
    /// <summary>容器键规范化（唯一形态：去首尾空白；空/纯空白 ⇒ 空串，调用方按"无容器"处理）。</summary>
    /// <param name="container">原始容器键。</param>
    public static string Normalize(string? container)
        => string.IsNullOrWhiteSpace(container) ? string.Empty : container.Trim();

    /// <summary>容器等价判据（比较面：归一后序数忽略大小写）。</summary>
    /// <param name="left">容器键。</param>
    /// <param name="right">容器键。</param>
    public static bool Equivalent(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
