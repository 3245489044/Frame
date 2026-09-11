using DataCenter.Protocol.Roots;

namespace DataCenter.Hosting;

/// <summary>
/// 业务取根唯一路径（<see cref="IRootProvider"/> 的宿主实现）—— 由 Hosting 持有并随换代交换指向。
/// 语义：未装配/已解绑 → <see cref="RootMissingException"/>（fail-fast，不给"静默造根"机会）。
/// 纪律（设计文档 §12.4）：构造注入 &gt; IRootProvider 注入 &gt; 装配期服务定位器；static 槽一律禁止。
/// </summary>
public sealed class HostRootProvider : IRootProvider
{
    private IDataCenterRoot? _root;

    /// <summary>当前代数根；未装配/已解绑 → 抛 <see cref="RootMissingException"/>。</summary>
    public IDataCenterRoot Root
        => Volatile.Read(ref _root) ?? throw RootMissingException.Throw(nameof(HostRootProvider));

    /// <summary>绑定根（Boot 装配 / 换代交换时调用；幂等覆盖）。</summary>
    internal void Bind(IDataCenterRoot root)
        => Volatile.Write(ref _root, root ?? throw new ArgumentNullException(nameof(root)));

    /// <summary>解绑（换取/停机窗口内主动断链，避免读到半迁移的根）。</summary>
    internal void Unbind() => Volatile.Write(ref _root, null);
}
