#nullable enable

namespace Share.Composition.Text;

/// <summary>空段判据 —— 两套判据语义不同，禁止互换（合并即改变输出）。</summary>
public enum EmptyPolicy
{
    /// <summary>保留空段（产生连续分隔符；对应原 <c>string.Join</c> 原义）。</summary>
    Keep,

    /// <summary>丢弃 <c>null</c>/空串（保留纯空白；对应 <c>string.IsNullOrEmpty</c>）。</summary>
    DropEmpty,

    /// <summary>丢弃 <c>null</c>/空串/纯空白（对应 <c>string.IsNullOrWhiteSpace</c>）。</summary>
    DropBlank,
}

/// <summary>片段裁剪口径。</summary>
public enum TrimPolicy
{
    /// <summary>原样（不裁剪）。</summary>
    None,

    /// <summary>逐片段 <c>Trim()</c>。</summary>
    Trim,

    /// <summary>纯空白片段归一为空串（不改动非空白片段的空白）。</summary>
    BlankToEmpty,
}

/// <summary>
/// 拼接策略 —— 把「分隔符 / 空段判据 / 裁剪口径」三轴显式化，替代多份平行拼接原语。
/// <para>三轴正交且封闭：新增拼接形态在此登记一个策略值，不再新增拼接方法。</para>
/// </summary>
/// <param name="Separator">片段分隔符（空串 = 首尾相接）。</param>
/// <param name="Empty">空段判据。</param>
/// <param name="Trim">片段裁剪口径。</param>
public sealed record JoinPolicy(string Separator, EmptyPolicy Empty = EmptyPolicy.DropEmpty, TrimPolicy Trim = TrimPolicy.None)
{
    /// <summary>空行分隔 + 丢弃空串（<c>IsNullOrEmpty</c> 判据）。</summary>
    public static JoinPolicy BlankLine { get; } = new("\n\n", EmptyPolicy.DropEmpty);

    /// <summary>空行分隔 + 保留空段（<c>string.Join("\n\n", …)</c> 原义）。</summary>
    public static JoinPolicy KeepAll { get; } = new("\n\n", EmptyPolicy.Keep);

    /// <summary>无分隔首尾相接（模板填充 / 原样拼接）。</summary>
    public static JoinPolicy Concat { get; } = new(string.Empty, EmptyPolicy.Keep);

    /// <summary>空行分隔 + 丢弃纯空白（<c>IsNullOrWhiteSpace</c> 判据；<b>不得</b>与 <see cref="BlankLine"/> 互换）。</summary>
    public static JoinPolicy BlankLineDropBlank { get; } = new("\n\n", EmptyPolicy.DropBlank);
}
