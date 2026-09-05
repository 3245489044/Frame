
#nullable enable

namespace FrameworkAbstractions.ContainerAbstractions;

/// <summary>
/// 容器访问端口（IContainerAccessPort）——前后端共用的唯一容器寻址口径。
/// BackEndDriven 验证/断言链（validate / assert container / validate-mount）全部经此端口解析，
/// 替代散落的 FindByName / FindRecursive / scope→hostKey 硬编码（收敛口径 4→1）。
/// 实现由 FrontEndDriven 提供（ContainerAccessPortAdapter），委托 ContainerRegistry + ControlPool，
/// 注册于 AppServiceProvider（循 A1 先例：接口放 Share、实现注册于前端）。
/// </summary>
public interface IContainerAccessPort
{
    /// <summary>按作用域强类型解析宿主。</summary>
    ContainerHandle? ResolveHost(string scope);

    /// <summary>按容器身份解析宿主。</summary>
    ContainerHandle? ResolveHost(ContainerId id);

    /// <summary>
    /// 按「作用域或宿主标识」统一解析宿主——先按 scope（"Side"），再按 hostKey（"sideContainer"）反查 scope 后解析。
    /// 消除 assert-container（ResolveHandle 带 hostKey 兜底）与 validate-mount（ResolveInHost 直调 ResolveHost）
    /// 两条断言链的寻址口径分裂（同一字符串 "sideContainer" 在两链认知不一致）。
    /// </summary>
    ContainerHandle? ResolveHostByKeyOrScope(string scopeOrHostKey);

    /// <summary>读取容器直接子控件数（读 MountState/ControlPool，不遍历树）。</summary>
    int? DirectChildCount(ContainerId id);

    /// <summary>读取挂载事实（替代 FindForm()!=null 推断）。</summary>
    bool IsMounted(ContainerId id);

    /// <summary>判定 child 是否 host 的后代（经 entity/控件父链判定）。</summary>
    bool IsDescendantOf(ContainerId child, ContainerId host);

    /// <summary>按宿主标识反查作用域（替代硬编码映射）。</summary>
    string? ResolveScopeByHostKey(string hostKey);
}
