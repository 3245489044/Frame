using System.Reflection;
using Strategy.Observability.Boundary;
using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口装配的**数据半注入面**（宿主只认内核接口与标准文本：端口键 / 实现 id 均为不透明字符串，
/// 本接口与实现方之间无领域类型往来）：端口标准、端口目录、绑定计划、逐行回填工厂全由此供给。
/// <para>
/// 分工：机制半 = <see cref="SpecPorts"/>（执行面）；数据半 = 领域实现（如 Agent 测试侧的端口计划）。
/// 资源位置（嵌入资源、产品台账路径）由实现方自知，故宿主不得持有任何领域命名空间引用。
/// </para>
/// </summary>
public interface ISpecPortPlans
{
    /// <summary>缺省端口标准（测试专用端口面数据源；资源位置由实现方自知）。</summary>
    SpecPortStandards Standards();

    /// <summary>
    /// 装配资源（端口目录 + 契约注册表 + 领域注入面）。调用方须保证**先于**绑定计划构造：
    /// 端口实现可能在构造期经迟绑定目录取依赖对象（<c>PortBackedProxy</c>）。
    /// </summary>
    SpecPortResources Resources(string? observeEventStorePath);

    /// <summary>
    /// 端口面投影（标准 + 已播种契约面 + 逐行回填工厂 + 本次装配的实现键 → <see cref="ISpecPortSurface"/>）。
    /// <para><paramref name="registeredKeys"/>：本次装配的已登记实现键（registry 视图；机制半据此构造内联实现面）。</para>
    /// </summary>
    ISpecPortSurface Surface(
        SpecPortStandards standards,
        IBoundaryContractRegistry contracts,
        Func<string, Func<IPortBinding>?> bindingFactory,
        IReadOnlyList<string> registeredKeys);

    /// <summary>
    /// 端口实现**族清单**（数据半声明；**声明序 = 登记序**）：机制半按此逐族填充本次装配的
    /// <see cref="SpecPortRegistry"/>（<see cref="SpecPortComposition.BuildRegistry"/>）。
    /// 族 = 纯登记函数（无副作用残留），可安全重复执行；新增一族 = 加 1 文件 + 本清单 1 行，机制半零改动。
    /// <para>缺省空集（不认识族装配的注入方零回归）。F1 起退化为**显式补充口**：常规实现改由
    /// <see cref="ImplementationAssemblies"/> 的标注扫描登记（动态键仍需本口）。</para>
    /// </summary>
    IReadOnlyList<Action<SpecPortRegistry>> Families() => Array.Empty<Action<SpecPortRegistry>>();

    /// <summary>
    /// 端口实现**程序集**（F1 声明驱动登记）：机制半扫描其中的实现标注并登记，免去逐行手写登记表
    /// （登记顺序契约随之消亡——键集合即事实）。反射只作用于本口给出的程序集（机制半不知领域位置）。
    /// <para>缺省空集（不认识标注的注入方零回归）。</para>
    /// </summary>
    IReadOnlyList<Assembly> ImplementationAssemblies() => Array.Empty<Assembly>();

    /// <summary>
    /// 端口**族目录**（F7 族声明单源）：按端口的族定位其实现登记点文案（诊断用）。
    /// 缺省 null → 回退 <see cref="ImplementationPoint"/> 的全局文案（零回归）。
    /// </summary>
    PortFamilyCatalog? FamilyCatalog() => null;

    /// <summary>
    /// 实现登记点描述（诊断文案：未绑定端口的原因指向「实现登记在哪」）：领域字样由数据半注入，
    /// 宿主缺省不含领域词汇（分层纪律）。
    /// </summary>
    string ImplementationPoint() => SpecPortPlanBuilder.DefaultImplementationPoint;

    /// <summary>
    /// 数据半**工厂表**（组合绑定的 <c>dep.factory</c> 用：工厂名 → 求值器，工厂名对机制半不透明）。
    /// 缺省空表（不提供任何工厂；声明引用了工厂而表缺失即装配期 fail-fast，不静默造空）。
    /// </summary>
    IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> Factories()
        => new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal);
}

/// <summary>
/// 端口装配资源（由数据半构造、执行面按内核接口消费）：目录与契约表为装配器输入，
/// 作用域 / 事件源为宿主注入面（null = 无作用域 / 仅 <c>Port(…)</c> 根）。
/// </summary>
public sealed record SpecPortResources(
    IPortCatalog Catalog,
    IBoundaryContractRegistry Contracts,
    ISpecScopeProvider? Scope = null,
    ISpecEventSource? Events = null);
