
#nullable enable

namespace FrameworkAbstractions.ContainerAbstractions;

/// <summary>
/// 容器角色（强类型分类，替代散落的字符串容器名判定）。
/// 注册时依据配置/类型归类，运行时全程走 <see cref="ContainerId"/> 引用。
/// </summary>
public enum ContainerRole
{
    Other,
    MainLayout,
    Content,
    Side,
    SplitterPanel,
}

/// <summary>
/// 挂载状态（MountState）——终结「推断 ≠ 事实」。
/// Registered=已注册未挂载；Mounted=已挂载到父容器（WinFormApplier 写入）；Orphaned=已卸载/孤儿。
/// 权威写入点：WinFormApplier.MarkMounted / EcsGate.MarkUnmounted；读取方不得再以 FindForm()!=null 推断。
/// </summary>
public enum MountState
{
    Registered,
    Mounted,
    Orphaned,
}

/// <summary>
/// 强类型容器身份（ContainerIdentity）——消灭散落的字符串 Name 匹配与 scope→hostKey 硬编码。
/// 字符串仅在注册边界出现一次；运行时全程以本记录引用。
/// 说明：Entity 锚点使用 <see cref="string"/>（EntityKey，注册时由实体标识归一生成一条），
/// 避免将本层（FrameworkAbstractions）绑定到 ECS 程序集，维持零额外依赖。
/// </summary>
public sealed record ContainerId
{
    /// <summary>作用域标识（"Side" / "Content" / "MainLayout"）。</summary>
    public required string ScopeId { get; init; }

    /// <summary>宿主标识（配置声明的 hostKey，注册时归一一次），如 "sideContainer"。</summary>
    public required string HostKey { get; init; }

    /// <summary>实体锚点字符串（原 Entity.ToString()，注册边界生成）。</summary>
    public required string EntityKey { get; init; }

    /// <summary>WinForms 控件名（仅注册边界使用；运行时优先 HostKey）。</summary>
    public required string ControlName { get; init; }

    /// <summary>容器角色。</summary>
    public ContainerRole Role { get; init; } = ContainerRole.Other;
}
