using DataCenter.Protocol.Standards;
using Strategy.Observability.Boundary;
using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口面（执行面通用件）：把端口标准（<see cref="SpecPortStandards"/>，宿主读取口）
/// 与端口目录的契约面投影为宿主的 <see cref="ISpecPortSurface"/>——于是宿主不认识标准文件格式。
/// <para>
/// 零领域词汇：端口键 / 步骤名 / 已登记实现键均为不透明字符串；内联实现面由**构造期注入**
/// （<paramref name="registeredKeys"/> = 本次装配的 registry 实现键），不再反读任何静态登记表单例。
/// </para>
/// </summary>
public sealed class SpecPortSurface : ISpecPortSurface
{
    private readonly SpecPortStandards _standards;
    private readonly IBoundaryContractRegistry _contracts;
    private readonly Func<string, Func<IPortBinding>?>? _bindingFactory;
    private readonly IReadOnlyList<string> _registeredKeys;
    private readonly Dictionary<string, string> _byStepName;

    /// <summary>
    /// <paramref name="bindingFactory"/>：装配期绑定工厂（P4 逐行替身隔离的回填来源；null = 不支持回填，
    /// 替身注入后不可撤销——仅用于不需要逐行隔离的场景）。
    /// <paramref name="registeredKeys"/>：已登记内联实现端口键（本次装配的 registry 视图；构造期快照）。
    /// </summary>
    public SpecPortSurface(
        SpecPortStandards standards,
        IBoundaryContractRegistry contracts,
        Func<string, Func<IPortBinding>?>? bindingFactory,
        IReadOnlyList<string> registeredKeys)
    {
        _standards = standards ?? throw new ArgumentNullException(nameof(standards));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _bindingFactory = bindingFactory;
        _registeredKeys = (registeredKeys ?? throw new ArgumentNullException(nameof(registeredKeys)))
            .OrderBy(key => key, StringComparer.Ordinal).ToArray();
        _byStepName = BuildStepIndex(standards);
    }

    /// <summary>路由面 = 标准端口键（Ordinal 升序）。**别名不得进入本面**（装配面 + 契约面均以端口键为准）。</summary>
    public IReadOnlyList<string> PortKeys => _standards.PortKeys;

    /// <summary>是否接管：端口键直接命中，或为已登记步骤名（历史 kind 名经别名索引归一）。</summary>
    public bool Handles(string portKey) => TryResolveStep(portKey, out _);

    /// <summary>
    /// 步骤名 → 端口键归一（路线 B）：端口键优先（键面与别名面撞名时键可解析），其次步骤名索引。
    /// </summary>
    public bool TryResolveStep(string step, out string portKey)
    {
        if (!string.IsNullOrEmpty(step))
        {
            if (_standards.TryGet(step, out _))
            {
                portKey = step;
                return true;
            }

            if (_byStepName.TryGetValue(step, out var canonical))
            {
                portKey = canonical;
                return true;
            }
        }

        portKey = step;
        return false;
    }

    public bool TryGetProductFields(string portKey, out string productFields)
    {
        productFields = string.Empty;
        if (!TryResolveStep(portKey, out var canonical)
            || !_standards.TryGet(canonical, out var standard)
            || (standard.Impl?.ProductFields ?? string.Empty).Length == 0) return false;

        productFields = standard.Impl!.ProductFields;
        return true;
    }

    /// <summary>内联实现面 = 构造期注入的实现键（本次装配 registry 视图）。</summary>
    public IReadOnlyList<string> InlinePortKeys => _registeredKeys;

    /// <summary>契约面 = 已播种的端口契约键（生产台账 ∪ 测试标准；含只登记未绑定者）。</summary>
    public IReadOnlyList<string> ContractKeys => _contracts.Ports.Select(c => c.Key).ToArray();

    /// <summary>装配期绑定工厂（P4 逐行替身隔离）：由装配入口 <see cref="SpecPorts"/> 按标准条目回填；键先归一。</summary>
    public Func<IPortBinding>? BindingFactory(string portKey)
        => TryResolveStep(portKey, out var canonical) ? _bindingFactory?.Invoke(canonical) : null;

    /// <summary>
    /// 步骤名索引（<c>impl.legacyKind</c> = spec 面入口名 → 端口键；Ordinal）：
    /// 重复即抛（同一步骤名映射到两个实现承载位 = 路由歧义，禁静默择一）。
    /// </summary>
    private static Dictionary<string, string> BuildStepIndex(SpecPortStandards standards)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var standard in standards.Entries)
        {
            if (standard.Impl?.LegacyKind is not { Length: > 0 } step) continue;

            if (!map.TryAdd(step, standard.Key))
                throw new InvalidOperationException(
                    $"[port:step] 步骤名 '{step}' 重复登记（{map[step]} 与 {standard.Key}）：spec 面入口名必须唯一");
        }

        return map;
    }
}
