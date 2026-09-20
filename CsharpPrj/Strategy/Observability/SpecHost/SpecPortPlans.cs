using System.Reflection;
using Strategy.Observability.Boundary;
using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口装配**数据半缺省实现**：只内建机制——标准资源定位（嵌入资源后缀）、端口面投影、
/// 实现程序集清单、工厂表转发；**领域资源构造**（产品端口目录 / 观测事件源）由调用方以委托注入。
/// <para>
/// 边界（分层纪律）：本类零领域词汇——端口键 / 资源后缀 / 工厂名均为不透明字符串，故数据半不必再自建
/// <see cref="ISpecPortPlans"/> 实现；族目录（F7）随族标注退场，缺省 null（回退全局实现登记点文案）。
/// </para>
/// </summary>
public sealed class SpecPortPlans : ISpecPortPlans
{
    private readonly Assembly _assembly;
    private readonly Func<string?, SpecPortResources> _resources;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _factories;
    private readonly string _implementationPoint;

    /// <summary>
    /// <paramref name="resources"/>：领域资源构造（入参 = 观测事件区路径，null = 未装配观测）；
    /// <paramref name="resourceSuffix"/>：**保留形参**（H3b 起标准载体 = 构建期常量，不再走嵌入资源；留参为二进制兼容）；
    /// <paramref name="factories"/>：具名工厂表（声明面不承载的领域构造；缺省空表 = 引用工厂即装配期 fail-fast）。
    /// </summary>
    public SpecPortPlans(
        Assembly assembly,
        Func<string?, SpecPortResources> resources,
        string resourceSuffix = "test-ports.json",
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>>? factories = null,
        string? implementationPoint = null)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceSuffix);

        _factories = factories
            ?? new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal);
        _implementationPoint = implementationPoint ?? SpecPortPlanBuilder.DefaultImplementationPoint;
    }

    /// <summary>
    /// 标准文档 JSON 常量（构建期由 <c>Standards\ports\*.specs</c> 生成）——
    /// **唯一装载源**（运行期零文件读取、零嵌入资源）。
    /// </summary>
    public string? StandardsJson { get; init; }

    /// <summary>标准资源 = 构建期常量（未注入即抛：不静默空表）。</summary>
    public SpecPortStandards Standards()
        => SpecPortStandards.Parse(StandardsJson
            ?? throw new InvalidOperationException(
                "[port:standard] 标准常量未注入：装配方须设置 SpecPortPlans.StandardsJson（源 = Standards\\ports\\*.specs 生成物）"));

    /// <summary>装配资源（领域侧构造：产品目录 + 契约注册表 + 作用域 / 事件源）。</summary>
    public SpecPortResources Resources(string? observeEventStorePath) => _resources(observeEventStorePath);

    /// <summary>端口面投影 = 执行面通用件（标准 + 契约面 + 回填工厂 + 本次装配实现键）。</summary>
    public ISpecPortSurface Surface(
        SpecPortStandards standards, IBoundaryContractRegistry contracts,
        Func<string, Func<IPortBinding>?> bindingFactory, IReadOnlyList<string> registeredKeys)
        => new SpecPortSurface(standards, contracts, bindingFactory, registeredKeys);

    /// <summary>实现程序集 = 本程序集（F1 声明驱动：机制半扫描其内实现标注，键集合即事实）。</summary>
    public IReadOnlyList<Assembly> ImplementationAssemblies() => new[] { _assembly };

    /// <summary>实现登记点文案（诊断兜底；领域字样由调用方注入）。</summary>
    public string ImplementationPoint() => _implementationPoint;

    /// <summary>具名工厂表（缺省空表：声明引用了工厂而表缺失即装配期 fail-fast，不静默造空）。</summary>
    public IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> Factories() => _factories;
}
