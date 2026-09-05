
#nullable enable

using System.Windows.Forms;

namespace FrameworkAbstractions.ContainerAbstractions;

/// <summary>
/// 容器访问句柄——ContainerRegistry 解析出口的强类型产物。
/// 承载身份 + 控件引用 + 挂载状态；前端 ContainerRegistry.Resolve* 返回。
/// </summary>
public sealed record ContainerHandle
{
    public required ContainerId Id { get; init; }

    /// <summary>实际控件引用（注册时关联；可能随挂载更新）。</summary>
    public Control? Control { get; init; }

    /// <summary>当前挂载状态。</summary>
    public MountState State { get; init; } = MountState.Registered;

    public bool IsMounted => State == MountState.Mounted && Control != null;
}
