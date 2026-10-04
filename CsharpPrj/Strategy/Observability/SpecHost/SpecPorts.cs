using Strategy.Observability.Boundary;
using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口装配选项（领域资源一律经 <see cref="ISpecPortPlans"/> 注入，故本类零领域类型）。
/// </summary>
public sealed record SpecPortOptions
{
    /// <summary>观测事件区文件路径（非空 = 装配观测，使 <c>Event/Timeline/Aggregate</c> 三根可取值）。</summary>
    public string? ObserveEventStorePath { get; init; }

    /// <summary>端口标准（缺省 = 数据半的 <see cref="ISpecPortPlans.Standards"/>）。</summary>
    public SpecPortStandards? Standards { get; init; }

    /// <summary>数据半注入面（必需：执行面不识别领域资源位置）。</summary>
    public ISpecPortPlans? Plans { get; init; }

    /// <summary>端口面投影（缺省 = 数据半的 <see cref="ISpecPortPlans.Surface"/>）。</summary>
    public ISpecPortSurface? Surface { get; init; }

    /// <summary>环境作用域提供者（缺省 = 数据半提供者；null = 无作用域）。</summary>
    public ISpecScopeProvider? Scope { get; init; }

    /// <summary>观测事件源（缺省 = 数据半提供者；null = 仅 <c>Port(…)</c> 根）。</summary>
    public ISpecEventSource? Events { get; init; }
}

/// <summary>
/// 端口装配入口（执行面通用件）：契约播种（生产面 ∪ 标准面）→ 绑定计划 →
/// 宿主构造 → 逐端口绑定 → 挂目录，数据半由 <see cref="ISpecPortPlans"/> 注入。
/// <para>
/// 装配 fail-safe：单个端口绑定失败只登记 <see cref="SpecPortHost.Unbound"/>，不阻断其余端口
/// （记账唯一权威 = 内核 <see cref="PortComposer"/>）。宿主可多次装配（每测试各一目录），
/// <see cref="ActiveCatalog"/> 随之刷新（族实现迟绑定取依赖对象，不锁死陈旧目录）。
/// </para>
/// <para>归位批 I2：<c>Mount(registry, …)</c>（测试内核注册表挂载）随宿主上移移交接线半——本件零测试依赖。</para>
/// </summary>
public static class SpecPorts
{
    private static IPortCatalog? _active;

    /// <summary>
    /// 依赖解析目录（P4-0 迟绑定源）：最近一次装配的端口目录，供族实现的 <c>PortBackedProxy</c> 取依赖对象。
    /// <para>语义锁定：**每次 <see cref="Assemble"/> 都刷新**；未装配即抛可定位异常（不返回空目录、不伪造绑定）。</para>
    /// </summary>
    public static IPortCatalog ActiveCatalog
        => Volatile.Read(ref _active)
           ?? throw new InvalidOperationException(
               "[spec:port] 端口目录未装配：须先经 SpecPorts.Assemble/Mount 装配（族实现经 PortBackedProxy 取依赖对象的迟绑定源）");

    /// <summary>装配端口宿主（守卫 / 诊断用）；每次调用刷新 <see cref="ActiveCatalog"/>。</summary>
    public static SpecPortHost Assemble(SpecPortOptions options)
    {
        PortGuard.Required(options, nameof(options));

        var plans = options.Plans ?? throw new InvalidOperationException(
            "[spec:port] 未注入数据半（SpecPortOptions.Plans）：执行面装配入口不识别领域资源位置（标准文件 / 端口目录 / 绑定计划）");

        var standards = options.Standards ?? plans.Standards();
        var resources = plans.Resources(options.ObserveEventStorePath);   // 目录就绪须先于实现登记（E-N5-12）

        var composer = new PortComposer(resources.Catalog, resources.Contracts);
        composer.Seed(SpecBoundaryContracts.Project(standards.Json));   // 测试专用端口契约面（经标准层投影；键重复者幂等跳过）

        // 实现登记：每装配新建 registry（即用即弃；幂等由结构保证，无静态门/锁）。
        var registry = SpecPortComposition.BuildRegistry(plans);

        var catalog = plans.FamilyCatalog();
        var planned = SpecPortPlanBuilder.Build(
            standards,
            id => registry.TryGet(id, out var invoke) ? invoke : null,
            plans.ImplementationPoint(),
            catalog is null ? null : catalog.ImplementationPointOf,
            plans.Factories());
        var byKey = planned.ToDictionary(p => p.PortKey, p => p, StringComparer.Ordinal);

        // 装配期绑定工厂（P4 逐行替身隔离回填）：同源于标准条目，避免第二套绑定逻辑；
        // 内联实现面 = 本次 registry 的实现键（构造期注入，端口面不再反读静态表）。
        var surface = options.Surface
            ?? plans.Surface(standards, resources.Contracts, key => BindingFactory(byKey, key), registry.PortIds);

        var host = new SpecPortHost(
            resources.Catalog,
            surface,
            composer,
            options.Scope ?? resources.Scope,
            options.Events ?? resources.Events);

        composer.Bind(planned);   // 逐端口装配 + fail-safe 记账（记账唯一权威 = 内核 PortComposer）

        Volatile.Write(ref _active, resources.Catalog);
        return host;
    }

    /// <summary>装配期绑定工厂：按计划重建未装饰绑定；冻结 / 未登记 → null（宿主据此跳过回填）。</summary>
    private static Func<IPortBinding>? BindingFactory(
        IReadOnlyDictionary<string, PortBinding> plans, string portKey)
    {
        if (!plans.TryGetValue(portKey, out var binding)) return null;

        var kind = binding.Kind;
        if (kind == "delegate") return binding.Factory;                              // 委派：原样回填
        if (kind is "reflective" or "composed" or "scripted") return binding.RequireBinding;  // 声明驱动：内核单点重建
        return null;   // frozen / 未知：本就不在可替换面
    }
}
