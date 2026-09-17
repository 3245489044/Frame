using Strategy.Observability.Boundary;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 声明式端口装配器（A2 装配收敛 · R5-1/R5-2；E15 / G1 · 原测试侧 <c>Bridge\PortRegistry.cs</c> 的装配职责上移）：
/// 「播种契约（标准目录投影）→ 逐端口装配 <see cref="PortBinding"/> → Unbound/Frozen 记账（fail-safe）」。
/// <para>
/// 纪律：单个端口绑定失败只记 <see cref="Unbound"/>，不阻断其余端口；记账是**唯一权威**（调用方不再自持一份）。
/// 内核只接受 JSON 文本与 <see cref="PortBinding"/> 计划，不读文件、不读嵌入资源（L0 不碰产品台账）。
/// </para>
/// <para>
/// 无种类分派：绑定类的构建归 <see cref="PortBinding.TryConstruct"/>（数据驱动 if/else），装配器只负责
/// 「取构建结果 + fail-safe 记账 + 契约登记」，**不做种类穷举**。
/// </para>
/// </summary>
public sealed class PortComposer
{
    private readonly IPortCatalog _catalog;
    private readonly IBoundaryContractRegistry _contracts;
    private readonly Dictionary<string, string> _unbound = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _frozen = new(StringComparer.Ordinal);

    public PortComposer(IPortCatalog catalog, IBoundaryContractRegistry contracts)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    /// <summary>绑定失败（fail-safe 记录）端口键 → 原因。</summary>
    public IReadOnlyDictionary<string, string> Unbound => _unbound;

    /// <summary>冻结（只登记未绑定）端口键 → 原因。</summary>
    public IReadOnlyDictionary<string, string> Frozen => _frozen;

    /// <summary>
    /// 播种契约（端口 / 数据 / 事件三类；幂等：已登记的键跳过）。返回新登记条数。
    /// <para>
    /// 入参 = **已装载的边界契约**（装配侧经标准目录投影：<c>StandardCatalog</c> →
    /// <see cref="BoundaryContractProjection"/>）。内核**不解析 JSON、不读文件**——装载期解析与校验
    /// 单一归 <c>DataCenter.Standards</c>。
    /// </para>
    /// </summary>
    public int Seed(IEnumerable<BoundaryContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        var added = 0;
        foreach (var contract in contracts)
            if (_contracts.TryRegister(contract)) added++;

        return added;
    }

    /// <summary>逐端口装配（fail-safe）。返回成功绑定数。</summary>
    public int Bind(IEnumerable<PortBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var bound = 0;
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);

            if (binding.Kind == "frozen")
            {
                MarkFrozen(binding.PortKey, binding.Reason ?? "只登记不绑定");
                continue;
            }

            try
            {
                if (BindOne(binding)) bound++;
            }
            catch (Exception ex)
            {
                MarkUnbound(binding.PortKey, $"{ex.GetType().Name}：{ex.Message}");
            }
        }

        return bound;
    }

    /// <summary>登记「未绑定」原因（fail-safe 记账入口）。</summary>
    public void MarkUnbound(string portKey, string reason) => _unbound[portKey] = reason;

    /// <summary>登记冻结端口（只登记未绑定：结构型端口转守卫面）。</summary>
    public void MarkFrozen(string portKey, string reason) => _frozen[portKey] = reason;

    /// <summary>单端口装配：取绑定构建结果；声明不完整 / 未落实现 / 未知种类 → 记 Unbound 并中止该端口。</summary>
    private bool BindOne(PortBinding binding)
    {
        var created = binding.TryConstruct(out var failReason);
        if (created is null)
        {
            MarkUnbound(binding.PortKey, failReason ?? "绑定声明不完整");
            return false;
        }

        return Bind(binding.PortKey, created);
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