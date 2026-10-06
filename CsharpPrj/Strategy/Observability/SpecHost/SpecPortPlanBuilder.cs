using DataCenter.Protocol.Standards;
using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 标准条目 → 绑定计划（机制半）：只认「标准模型 + 实现查询 + 实现登记点文案」三件，
/// **不认识族 / 文件 / 领域命名空间**（故实现登记点描述由调用方注入，本类不含领域词汇）。
/// <para>
/// 归位批 I2，批 1 收敛：本类**直读标准模型**（<see cref="PortStandard"/> / <see cref="PortImplStandard"/>），
/// 声明驱动族（reflective / composed / scripted）一律经 <c>PortSpecCompiler.Compile</c> 编为**唯一规格**
/// （③ 声明镜像层与生成物映射已随批 1 退场，装配面只有一份声明形态）。
/// </para>
/// </summary>
public static class SpecPortPlanBuilder
{
    /// <summary>缺省实现登记点描述（诊断文案兜底；领域字样由数据半注入）。</summary>
    public const string DefaultImplementationPoint = "实现登记表";

    /// <summary>缺省工厂表（未注入时组合绑定的 <c>dep.factory</c> 一律 fail-fast，不静默造空）。</summary>
    private static readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> NoFactories =
        new Dictionary<string, Func<IReadOnlyList<object?>, object?>>(StringComparer.Ordinal);

    /// <summary>
    /// 逐标准条目产出绑定计划（端口键 Ordinal 升序，与 <see cref="SpecPortStandards.Entries"/> 同序）：
    /// <c>frozen</c> = 只登记（结构型转守卫面）；<c>reflective</c> = 声明驱动；<c>delegate</c> = 命中工厂，否则记名未绑定。
    /// <para>
    /// 产出载体统一为内核 <see cref="PortBinding"/>（计划构造点合一）：本类只做「标准条目 → 声明」投影，
    /// 绑定类构建一律下沉 <see cref="PortBinding"/> 的静态工厂（内核唯一构造点）。
    /// <paramref name="resolveImplementation"/> = 本次装配的实现查询（registry 查表）；未命中 → <c>null</c>
    /// （装配期 fail-safe 记入 <c>Unbound</c>，原因文案含实现登记点）。
    /// <paramref name="implementationPointOf"/>：可选「端口键 → 该端口所在族的实现登记点文案」（F7 族声明单源）；
    /// 返回 null 或未提供 → 回退 <paramref name="implementationPoint"/> 全局文案（零回归）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<PortBinding> Build(
        SpecPortStandards standards,
        Func<string, Func<IPortInput, object?>?> resolveImplementation,
        string implementationPoint = DefaultImplementationPoint,
        Func<string, string?>? implementationPointOf = null,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>>? factories = null)
    {
        PortGuard.Required(standards, nameof(standards));
        PortGuard.Required(resolveImplementation, nameof(resolveImplementation));

        var plans = new List<PortBinding>();

        foreach (var standard in standards.Entries)
        {
            var portKey = standard.Key;
            var impl = standard.Impl;

            switch (SpecPortStandards.KindOf(standard))
            {
                case SpecPortBindingKind.Frozen:
                    plans.Add(PortBinding.Frozen(
                        portKey, impl?.Reason ?? "结构型端口：只登记不绑定，结构断言迁守卫面"));
                    break;

                // 声明驱动族（reflective / composed / scripted）：**唯一规格**（① → 指令序）即装配面全部输入；
                // ③ 绑定声明家族退场后不再有第二份声明形态（批 1 收敛）。
                case SpecPortBindingKind.Reflective:
                case SpecPortBindingKind.Composed:
                case SpecPortBindingKind.Scripted:
                    plans.Add(PortBinding.FromSpec(portKey, PortSpecCompiler.Compile(standard), Injection(factories)));
                    break;

                default:
                    var id = impl?.Id ?? portKey;
                    var invoke = resolveImplementation(id);
                    var point = implementationPointOf?.Invoke(portKey) ?? implementationPoint;
                    plans.Add(invoke is null
                        ? PortBinding.Unbound(
                            portKey, id,
                            $"标准声明 impl.kind=delegate，但 {point} 未登记实现")
                        : PortBinding.Delegate(portKey, id, () => StandardFakes.Recording(invoke)));
                    break;
            }
        }

        return plans;
    }

    /// <summary>
    /// 装配产物注入袋（I5c/J4b 接线）：数据半工厂表随绑定下发执行侧 —— 声明模型与 IR 零运行期成员，
    /// 而 <c>dep.factory</c> 的求值仍可解析（缺表且声明了工厂即 fail-fast）。
    /// </summary>
    private static PortInjection Injection(
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>>? factories)
        => factories is null ? PortInjection.Empty : new PortInjection { Factories = factories };
}
