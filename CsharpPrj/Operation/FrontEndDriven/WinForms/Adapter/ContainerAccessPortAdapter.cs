#nullable enable

using System.Windows.Forms;
using FrameworkAbstractions.ContainerAbstractions;
using FrontEndDriven.Container;

namespace FrontEndDriven.WinForms.Adapter;

/// <summary>
/// 容器访问端口实现（ContainerAccessPortAdapter）——前后端共用容器寻址的唯一访问口径。
/// 委托 <see cref="ContainerRegistry"/>（容器事实源）+ 控件树；注册于 AppServiceProvider（循 A1 先例）。
/// BackEndDriven 验证/断言链（assert container / validate-mount / validate）经
/// <see cref="ContainerAssertionService"/> 使用本端口，替代散落 FindByName/FindRecursive/硬编码映射。
/// </summary>
public sealed class ContainerAccessPortAdapter : IContainerAccessPort
{
    private readonly HostMountService _hostMount;

    public ContainerAccessPortAdapter(HostMountService hostMount)
    {
        _hostMount = hostMount ?? throw new System.ArgumentNullException(nameof(hostMount));
    }

    public ContainerHandle? ResolveHost(string scope)
        => _hostMount.ResolveByScope(scope);

    public ContainerHandle? ResolveHost(ContainerId id)
    {
        // 先按 HostKey 精确命中，未命中再回退 scope——避免「接收完整标识却只用一半」导致同 scope 不同 hostKey 解析错宿主。
        return _hostMount.ResolveByHostKey(id.HostKey)
               ?? _hostMount.ResolveByScope(id.ScopeId);
    }

    public ContainerHandle? ResolveHostByKeyOrScope(string scopeOrHostKey)
    {
        // 唯一实现：HostMountService.ResolveHost（转发 ContainerRegistry.ResolveByScopeOrHostKey，scope 优先 + hostKey 反查）。
        // 收敛：消除本端口手写反查，与物理挂载同源，杜绝「一处改两处忘」漂移。
        return _hostMount.ResolveByScopeOrHostKey(scopeOrHostKey);
    }

    public int? DirectChildCount(ContainerId id)
        => _hostMount.GetDirectChildCount(id.HostKey);

    public bool IsMounted(ContainerId id)
        => _hostMount.IsMounted(id);

    public bool IsDescendantOf(ContainerId child, ContainerId host)
    {
        var childControl = _hostMount.ResolveHost(child.HostKey);
        var hostControl = _hostMount.ResolveHost(host.HostKey);
        if (childControl == null || hostControl == null) return false;

        // 祖先上溯统一到 ControlTraverser 原语（语义等价原手写 Parent 链）
        return Share.ControlLookup.ControlTraverser.IsDescendantOf(childControl, hostControl);
    }

    public string? ResolveScopeByHostKey(string hostKey)
        => _hostMount.ResolveScopeByHostKey(hostKey);
}
