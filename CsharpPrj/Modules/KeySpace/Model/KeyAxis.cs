namespace Modules.KeySpace.Model;

/// <summary>
/// 键轴（占位符名的语义别名）：轴名即模板占位符名，由声明面给定。
/// <para>
/// 存在理由（T2）：轴是**开放受控词**——新增轴 = 声明侧新增占位符，框架零改动；
/// 故本类型只承载名字与结构相等，不内置任何域词（域词由产品侧常量提供）。
/// </para>
/// </summary>
public readonly record struct KeyAxis(string Name)
{
    /// <summary>构造轴（空名即抛：轴无名无法定位占位符）。</summary>
    public static KeyAxis Of(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new KeyAxis(name);
    }

    /// <summary>是否为空轴（default 实例）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}
