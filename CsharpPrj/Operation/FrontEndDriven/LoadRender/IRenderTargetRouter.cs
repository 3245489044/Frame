#nullable enable

using FrontEndDriven.Container;
using FrontEndDriven.Shared;
using Share.StateManagement;
using System;
using TConfig;

namespace FrontEndDriven.LoadRender;

/// <summary>渲染目标动作（F3·「主布局壳误载内容」）。</summary>
public enum RenderTargetAction
{
    /// <summary>渲染进目标作用域。</summary>
    Render,

    /// <summary>幂等跳过（壳已是当前根壳，重复加载无害）。</summary>
    IdempotentSkip,

    /// <summary>拒绝（壳配置不能作为内容注入其自声明的宿主）。</summary>
    Reject
}

/// <summary>渲染目标（路由结果）。</summary>
public sealed record RenderTarget(RenderTargetAction Action, string? Scope, string? Message)
{
    public static RenderTarget Render(string scope) => new(RenderTargetAction.Render, scope, null);

    public static RenderTarget Skip(string message) => new(RenderTargetAction.IdempotentSkip, null, message);

    public static RenderTarget Reject(string message) => new(RenderTargetAction.Reject, null, message);
}

/// <summary>
/// 渲染目标路由（F3·消灭 D2 硬编码）：按配置角色 + 当前装配决定 load 加载目标，
/// 取代 ConfigLoadRenderService.ResolveContentScope() 无条件定 Content 的隐性契约。
/// </summary>
public interface IRenderTargetRouter
{
    RenderTarget Resolve(UIConfiguration config, string configPath);
}

/// <summary>
/// 默认路由实现：
///   Content 配置 → 主内容区（contentContainer 绑定 scope，与现状等价）。
///   Shell 配置 → 若该 shell 已是当前根壳（boot 已挂载于其宿主）→ 幂等跳过；
///                否则拒绝（作为内容注入其自声明的宿主 → 循环宿主注入，交 F1 闸门兜底）。
/// </summary>
public sealed class RenderTargetRouter : IRenderTargetRouter
{
    /// <summary>主内容容器名（Content 宿主；与 UIControlNames.ContentContainer 同值，FrontEndDriven 不引用应用层常量）。</summary>
    private const string ContentContainerHostKey = "contentContainer";

    /// <summary>主内容 scope 名（contentContainer 绑定 scope 反查失败时的回退字面量）。</summary>
    private const string ContentScopeFallback = "Content";

    private readonly HostMountService _hostMount;

    public RenderTargetRouter(HostMountService hostMount)
    {
        _hostMount = hostMount ?? throw new ArgumentNullException(nameof(hostMount));
    }

    public RenderTarget Resolve(UIConfiguration config, string configPath)
    {
        if (config == null)
            return RenderTarget.Reject("配置为空，无法路由渲染目标");

        var role = ConfigRoleResolver.GetRole(config);
        if (role == ConfigRole.Content)
        {
            // Content 配置 → 主内容区（与现状等价）
            var scope = ResolveContentScope();
            return string.IsNullOrWhiteSpace(scope)
                ? RenderTarget.Reject($"未找到主内容区宿主（{ContentScopeFallback}）")
                : RenderTarget.Render(scope!);
        }

        // Shell 配置：若已是当前根壳 → 幂等跳过；否则拒绝
        var ownScope = ResolveShellOwnScope(config);
        if (!string.IsNullOrWhiteSpace(ownScope) && IsMounted(ownScope))
        {
            return RenderTarget.Skip($"壳 {configPath} 已作为根壳挂载于 scope={ownScope}，幂等跳过（重复加载无害）");
        }

        return RenderTarget.Reject(
            $"配置 {configPath} 为宿主壳，需作为根壳（boot）渲染，不能作为内容注入其自声明的宿主 → 循环宿主注入已拦截。");
    }

    /// <summary>壳自身的根作用域 = boot[0].Scope（如 MainLayout 的 boot[0].scope="MainLayout"）。</summary>
    private static string? ResolveShellOwnScope(UIConfiguration config)
    {
        if (config.Boot is { Count: > 0 })
        {
            foreach (var phase in config.Boot)
            {
                if (!string.IsNullOrWhiteSpace(phase.Scope))
                    return phase.Scope;
            }
        }

        return null;
    }

    /// <summary>解析主内容区 scope：contentContainer 绑定 scope 反查（如 "Content"），反查失败回退字面 "Content"。</summary>
    private string? ResolveContentScope()
    {
        var scope = _hostMount.ResolveScopeByHostKey(ContentContainerHostKey);
        return string.IsNullOrWhiteSpace(scope) ? ContentScopeFallback : scope;
    }

    private bool IsMounted(string scope)
    {
        try
        {
            return _hostMount.GetMountState(scope) == ContainerMountState.Mounted;
        }
        catch
        {
            return false;
        }
    }
}
