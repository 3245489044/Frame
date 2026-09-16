using Strategy.Observability.Boundary;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>已解析的端口句柄（E10）：契约 + 当前绑定（真实实现 / 替身 / 装饰器）。</summary>
public sealed class PortHandle
{
    public PortHandle(BoundaryContract contract, IPortBinding binding)
    {
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }

    /// <summary>端口契约。</summary>
    public BoundaryContract Contract { get; }

    /// <summary>当前绑定。</summary>
    public IPortBinding Binding { get; internal set; }

    /// <summary>端口键。</summary>
    public string Key => Contract.Key;
}

/// <summary>
/// 端口目录（E10）：全库<b>唯一注册点</b>——生产实现与测试替身在此同址登记/替换，
/// 未登记即抛（含能力清单），重复登记即抛（单一实现点）。
/// </summary>
public interface IPortCatalog
{
    /// <summary>登记契约 + 绑定；同键重复登记抛 <see cref="DuplicateBoundaryException"/>。</summary>
    PortHandle Register(BoundaryContract contract, IPortBinding binding);

    /// <summary>登记契约 + 绑定；同键已存在返回 false。</summary>
    bool TryRegister(BoundaryContract contract, IPortBinding binding);

    /// <summary>按键解析；未登记抛 <see cref="PortNotRegisteredException"/>（含能力清单）。</summary>
    PortHandle Resolve(string portKey);

    bool TryResolve(string portKey, out PortHandle handle);

    /// <summary>替换已登记端口的绑定（测试侧替身注入；未登记抛 <see cref="PortNotRegisteredException"/>）。</summary>
    void Replace(string portKey, IPortBinding binding);

    /// <summary>已绑定端口的契约（按键 Ordinal 升序）；边界键集全量为 <see cref="IBoundaryContractRegistry.Contracts"/>。</summary>
    IReadOnlyList<BoundaryContract> Contracts { get; }

    /// <summary>调用记录（端口调用的单一记录点）。</summary>
    CallLog Log { get; }
}
