using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口装配组合：把数据半声明的**实现程序集标注**（F1 声明驱动）与
/// **族清单**（显式补充口）填充进本次装配的 <see cref="SpecPortRegistry"/>。
/// <para>
/// registry 由本类**每装配现场新建、即用即弃** ⇒ 跨装配零共享可变状态（「多次装配不抛」由结构保证，
/// 不再依赖静态幂等门）；标注扫描与族函数均为纯登记（无副作用残留），可安全重复执行。
/// </para>
/// </summary>
public static class SpecPortComposition
{
    /// <summary>
    /// 新建 registry 并登记：**先**按实现程序集扫描标注（F1：键集合即事实，顺序无契约），
    /// **再**执行族清单（显式补充口：动态键 / 标注不可达者；缺省空集）。
    /// </summary>
    public static SpecPortRegistry BuildRegistry(ISpecPortPlans plans)
    {
        PortGuard.Required(plans, nameof(plans));

        var registry = new SpecPortRegistry();

        foreach (var assembly in plans.ImplementationAssemblies())
            PortImplementationRegistry.RegisterInto(assembly, registry.Register);

        foreach (var family in plans.Families()) family(registry);

        return registry;
    }
}
