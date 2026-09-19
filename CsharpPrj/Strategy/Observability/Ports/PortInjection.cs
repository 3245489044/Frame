using DataCenter.Protocol.Standards;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口**装配产物注入袋**（I5c / J4a）：把「数据半注入面」（工厂表 / 场景选择等**运行期成员**）从声明模型与 IR 中
/// 彻底摘出，改由本类在**装配期**承载并随绑定实例传递。
/// <para>
/// 分工红线：<c>DataCenter.Protocol.Standards</c> 的 <c>*Standard</c> 声明模型与内核 IR（<see cref="PortSpec"/> /
/// <see cref="PortInstr"/>）**零运行期成员**（不出现 <c>Func&lt;&gt;</c>）——注入袋是「装配产物，不是数据」，
/// 只存在于执行侧（本件）。
/// </para>
/// <para>
/// 值的来源仍是声明面（<c>deps[].factory</c> 给出工厂名 + 元数），本袋只承载「名字 → 求值器」的绑定结果；
/// 缺表且声明了 <c>factory</c> 即 fail-fast（不静默造空）。
/// </para>
/// </summary>
public sealed class PortInjection
{
    private static readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> NoFactories =
        new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal);

    /// <summary>空注入袋（未注入 = 声明了 factory 即 fail-fast；零回归）。</summary>
    public static PortInjection Empty { get; } = new();

    /// <summary>
    /// 数据半工厂表（<c>dep.factory</c> 用；工厂名 → 求值器）。随装配传递，使「声明 → 依赖构建」不需要
    /// 第二套装配上下文（缺表且声明了 <c>factory</c> 即 fail-fast）。
    /// </summary>
    public IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> Factories { get; init; } = NoFactories;
}