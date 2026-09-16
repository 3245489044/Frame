using Strategy.Observability.Boundary;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>绑定计划种类（声明式装配的四种归宿）。</summary>
public enum PortBindingPlanKind
{
    /// <summary>反射绑定（按 <see cref="PortBindingSpec"/> 的 target/method/args 调用）。</summary>
    Reflective,

    /// <summary>组合绑定（按 <see cref="ComposedBindingSpec"/> 的 calls/project 多调用 + 投影）。</summary>
    Composed,

    /// <summary>脚本绑定（按 <see cref="ScriptedBindingSpec"/> 的 bind/deps/script/project/deliver 编排求值）。</summary>
    Scripted,

    /// <summary>委托绑定（由调用方给出实现工厂，如产品真实实现 / 测试内联编排）。</summary>
    Delegate,

    /// <summary>冻结（只登记不绑定：结构型端口转守卫面）。</summary>
    Frozen,
}

/// <summary>
/// 端口绑定计划：<c>端口键 → 绑定方式</c>（+ 反射声明 / 组合声明 / 实现工厂 / 冻结或未绑定原因）。
/// <para>调用方（产品组合根 / 测试驱动）负责从台账构造计划；内核只按计划装配，不读产品文件。</para>
/// </summary>
public sealed record PortBindingPlan(
    string PortKey,
    PortBindingPlanKind Kind,
    PortBindingSpec? Spec = null,
    Func<IPortBinding>? Factory = null,
    string? Reason = null,
    ComposedBindingSpec? Composed = null,
    ScriptedBindingSpec? Scripted = null);

/// <summary>
/// 声明式端口装配器（E15 / G1 · 原测试侧 <c>Bridge\PortRegistry.cs</c> 的装配职责上移）：
/// 「播种契约（文本）→ 逐端口按计划绑定 → Unbound/Frozen 记账（fail-safe）」。
/// <para>
/// 纪律：单个端口绑定失败只记 <see cref="Unbound"/>，不阻断其余端口；记账是**唯一权威**（调用方不再自持一份）。
/// 内核只接受 JSON 文本与绑定计划，不读文件、不读嵌入资源（L0 不碰产品台账）。
/// </para>
/// </summary>
public sealed class PortAssembler
{
    private readonly IPortCatalog _catalog;
    private readonly IBoundaryContractRegistry _contracts;
    private readonly Dictionary<string, string> _unbound = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _frozen = new(StringComparer.Ordinal);

    public PortAssembler(IPortCatalog catalog, IBoundaryContractRegistry contracts)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    /// <summary>绑定失败（fail-safe 记录）端口键 → 原因。</summary>
    public IReadOnlyDictionary<string, string> Unbound => _unbound;

    /// <summary>冻结（只登记未绑定）端口键 → 原因。</summary>
    public IReadOnlyDictionary<string, string> Frozen => _frozen;

    /// <summary>播种契约（端口 / 数据 / 事件三类；幂等：已登记的键跳过）。返回新登记条数。</summary>
    public int Seed(string contractsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractsJson);

        var added = 0;
        foreach (var contract in BoundaryContractJson.Load(contractsJson))
            if (_contracts.TryRegister(contract)) added++;

        return added;
    }

    /// <summary>逐端口装配（fail-safe）。返回成功绑定数。</summary>
    public int Bind(IEnumerable<PortBindingPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var bound = 0;
        foreach (var plan in plans)
        {
            ArgumentNullException.ThrowIfNull(plan);

            if (plan.Kind == PortBindingPlanKind.Frozen)
            {
                MarkFrozen(plan.PortKey, plan.Reason ?? "只登记不绑定");
                continue;
            }

            try
            {
                if (BindOne(plan)) bound++;
            }
            catch (Exception ex)
            {
                MarkUnbound(plan.PortKey, $"{ex.GetType().Name}：{ex.Message}");
            }
        }

        return bound;
    }

    /// <summary>登记「未绑定」原因（fail-safe 记账入口）。</summary>
    public void MarkUnbound(string portKey, string reason) => _unbound[portKey] = reason;

    /// <summary>登记冻结端口（只登记未绑定：结构型端口转守卫面）。</summary>
    public void MarkFrozen(string portKey, string reason) => _frozen[portKey] = reason;

    /// <summary>单端口装配：按计划择绑定方式；计划不完整 → 记 Unbound 并中止该端口。</summary>
    private bool BindOne(PortBindingPlan plan)
    {
        switch (plan.Kind)
        {
            case PortBindingPlanKind.Reflective:
                if (plan.Spec is null)
                {
                    MarkUnbound(plan.PortKey, "声明 reflective 绑定但缺少绑定声明（PortBindingSpec）");
                    return false;
                }

                return Bind(plan.PortKey, new ReflectiveBinding(plan.Spec));

            case PortBindingPlanKind.Composed:
                if (plan.Composed is null)
                {
                    MarkUnbound(plan.PortKey, "声明 composed 绑定但缺少组合声明（ComposedBindingSpec）");
                    return false;
                }

                return Bind(plan.PortKey, new ComposedBinding(plan.Composed));

            case PortBindingPlanKind.Scripted:
                if (plan.Scripted is null)
                {
                    MarkUnbound(plan.PortKey, "声明 scripted 绑定但缺少脚本声明（ScriptedBindingSpec）");
                    return false;
                }

                return Bind(plan.PortKey, new ScriptedBinding(plan.Scripted));

            case PortBindingPlanKind.Delegate:
                if (plan.Factory is null)
                {
                    MarkUnbound(plan.PortKey, plan.Reason ?? "声明 delegate 绑定但未提供实现工厂");
                    return false;
                }

                return Bind(plan.PortKey, plan.Factory());

            default:
                MarkUnbound(plan.PortKey, $"未知绑定计划种类 '{plan.Kind}'");
                return false;
        }
    }

    /// <summary>登记绑定：契约未播种即抛（防绑定未声明边界）；同键重复绑定即抛（单一实现点）。</summary>
    private bool Bind(string portKey, IPortBinding binding)
    {
        var contract = _contracts.Require(portKey);
        if (!_catalog.TryRegister(contract, binding))
            throw new DuplicateBoundaryException(portKey);

        return true;
    }
}
