#nullable enable

using System;

namespace Share.Composition.Text;

/// <summary>
/// 段键 —— 不透明命名空间键。内核不解码键的语义（<c>prompt.l0</c> / <c>report.header</c> 一律等价），
/// 领域侧负责命名与命名空间化，使同一内核可服务任意文本装配场景。
/// </summary>
public readonly record struct TextKey(string Value)
{
    /// <summary>空键（未指定）。</summary>
    public static TextKey Empty => new(string.Empty);

    /// <summary>null → 空串（键不参与空值语义，空键即未指定）。</summary>
    public static TextKey From(string? value) => new(value ?? string.Empty);

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;
}
