
namespace TConfig.Controls;

/// <summary>
/// 值访问器（类型差异的单点实现）：Reader 读物理控件当前值；
/// Writer 写物理控件并返回"写后规范值"（如 ListView 匹配选中后的 Tag 完整路径），
/// 保证写后读一致性（写 ECS 的值 = 读物理控件回读到的值）。
/// </summary>
public sealed record ValueAccessor(
    Func<object, object?> Reader,
    Func<object, object?, string?> Writer)
{
    /// <summary>强类型工厂：把类型化访问器包装为 object 签名（消除方法组转换限制）。</summary>
    public static ValueAccessor Create<TControl>(
        Func<TControl, object?> reader,
        Func<TControl, object?, string?> writer)
        where TControl : class
        => new(
            c => reader((TControl)c),
            (c, v) => writer((TControl)c, v));

    /// <summary>便捷工厂：writer 不产生规范值（如 Content 语义）时返回 null。</summary>
    public static ValueAccessor Create(
        Func<object, object?> reader,
        Action<object, object?> writer)
        => new(reader, (control, value) =>
        {
            writer(control, value);
            return null;
        });
}

