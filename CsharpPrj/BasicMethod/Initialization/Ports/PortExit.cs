namespace BasicMethod.Initialization.Ports;

/// <summary>
/// 端口的受控出口（对齐 axioms T/N/D 三出口）。
/// 声明式端口把「必填/可选/可降级」从命令式 <c>?? throw</c> 提升为声明，出口语义由本枚举统一裁决。
/// </summary>
public enum PortExit
{
    /// <summary>T：必填端口。缺省即 fail-fast（装配期）。</summary>
    Required,

    /// <summary>N：可选端口。缺省用命名空对象（NullXxx）。</summary>
    NullObject,

    /// <summary>D：有界降级。缺省即显式降级 + 可见化。</summary>
    Degrade,
}
