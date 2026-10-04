namespace BasicMethod.Initialization.Ports;

/// <summary>
/// 声明式端口槽：把「必填/可选/可降级」从命令式 <c>?? throw</c> / <c>?? new</c> 提升为声明。
/// 绑定表达式只写一次，T/N/D 语义由 <see cref="PortExit"/> 统一裁决。
/// </summary>
public readonly struct PortSlot<T> where T : class
{
    /// <summary>已绑定值（未绑定时为 null）。</summary>
    public T? Value { get; }

    /// <summary>端口名（用于 fail-fast 诊断与簿记）。</summary>
    public string Name { get; }

    /// <summary>受控出口（T/N/D）。</summary>
    public PortExit Exit { get; }

    private PortSlot(string name, T? value, PortExit exit)
    {
        Name = name;
        Value = value;
        Exit = exit;
    }

    /// <summary>T：必填端口。缺省即 fail-fast（装配期）。</summary>
    public static PortSlot<T> Required(string name) => new(name, null, PortExit.Required);

    /// <summary>N：可选端口，缺省用命名空对象（NullXxx.Instance）。</summary>
    public static PortSlot<T> Optional(string name) => new(name, null, PortExit.NullObject);

    /// <summary>D：有界降级，缺省即显式降级 + 可见化。</summary>
    public static PortSlot<T> Degradable(string name) => new(name, null, PortExit.Degrade);

    /// <summary>以实参填充槽位（装配期唯一赋值点，保持出口语义不变）。</summary>
    public PortSlot<T> Bind(T? value) => new(Name, value, Exit);
}
