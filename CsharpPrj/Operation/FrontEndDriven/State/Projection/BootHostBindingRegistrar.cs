using FrontEndDriven.Container;
using FrontEndDriven.Diagnostics;
using FrameworkAbstractions.ContainerAbstractions;
using TConfig;

namespace FrontEndDriven.State.Projection;

public static class BootHostBindingRegistrar
{
    public const string StateSourceType = "state";

    // 绑定来源优先级唯一权威: state 容器声明(json-declared) > boot 兜底(boot-declared) > code-registered
    // state 容器是唯一事实源: 同 scope 冲突时以 state 声明覆盖 boot/代码绑定(见 BindAuthoritative)
    // boot 仅作兜底: 为无 state 容器声明的启动阶段 scope(如 MainLayout)提供绑定(见 BindIfAbsent)
    private const string SourceBoot = "boot-declared";
    private const string SourceState = "json-declared";

    /// <summary>
    /// 从配置扫描并注册容器到 <see cref="ContainerRegistry"/>（S2：强类型注册，不再裸绑定 HostBindingStore）。
    /// </summary>
    public static void RegisterFromConfig(UIConfiguration config, ContainerRegistry containerRegistry)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(containerRegistry);

        if (config.Boot is { Count: > 0 })
        {
            foreach (var phase in config.Boot)
            {
                if (string.IsNullOrWhiteSpace(phase.Scope) || string.IsNullOrWhiteSpace(phase.Host))
                    continue;
                BindIfAbsent(containerRegistry, phase.Scope!, phase.Host);
            }
        }

        foreach (var viewConfig in config.Views)
        {
            if (viewConfig.Controls == null) continue;
            ScanControls(viewConfig.Controls, containerRegistry);
        }
    }

    // 兜底绑定(boot 声明): 仅当 scope 未注册才注册; 已注册则不覆盖(state 容器声明优先)
    private static void BindIfAbsent(ContainerRegistry containerRegistry, string scope, string hostKey)
    {
        if (containerRegistry.ResolveByScope(scope) == null)
        {
            containerRegistry.Register(scope, hostKey, role: RoleOf(scope), source: SourceBoot);
        }
    }

    // 权威绑定(state 容器声明): 唯一事实源, 同 scope host 冲突时覆盖既有绑定
    private static void BindAuthoritative(ContainerRegistry containerRegistry, string scope, string hostKey)
    {
        var existing = containerRegistry.ResolveByScope(scope);
        if (existing == null)
        {
            containerRegistry.Register(scope, hostKey, role: RoleOf(scope), source: SourceState);
            return;
        }

        if (string.Equals(existing.Id.HostKey, hostKey, StringComparison.OrdinalIgnoreCase))
        {
            // host 一致: 仅提升来源标记为事实源声明（重注册覆盖）
            containerRegistry.Register(scope, hostKey, role: RoleOf(scope), source: SourceState);
            return;
        }

        FrontEndDrivenLog.Log("HostBindingRegistrar_Override", new
        {
            scope,
            previousHost = existing.Id.HostKey,
            authoritativeHost = hostKey,
            note = "state 容器声明覆盖 boot/代码绑定，state 容器为唯一事实源，请检查 boot 声明是否冗余"
        });
        containerRegistry.Register(scope, hostKey, role: RoleOf(scope), source: SourceState);
    }

    private static void ScanControls(IEnumerable<ControlConfig> controls, ContainerRegistry containerRegistry)
    {
        foreach (var control in controls)
        {
            if (control.Panel1Controls != null) ScanControls(control.Panel1Controls, containerRegistry);
            if (control.Panel2Controls != null) ScanControls(control.Panel2Controls, containerRegistry);
            if (control.Tabs != null)
            {
                foreach (var tab in control.Tabs)
                {
                    if (tab.Controls != null) ScanControls(tab.Controls, containerRegistry);
                }
            }
            if (control.Controls != null) ScanControls(control.Controls, containerRegistry);

            if (control.Container is { } container
                && string.Equals(container.SourceType, StateSourceType, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(container.StateScope))
            {
                BindAuthoritative(containerRegistry, container.StateScope!, control.Name);
            }
        }
    }

    /// <summary>按 scope 派生容器角色（字符串仅在注册边界出现一次；运行时全程强类型）。</summary>
    private static ContainerRole RoleOf(string scope)
        => scope switch
        {
            _ when string.Equals(scope, "MainLayout", StringComparison.OrdinalIgnoreCase) => ContainerRole.MainLayout,
            _ when string.Equals(scope, "Content", StringComparison.OrdinalIgnoreCase) => ContainerRole.Content,
            _ when string.Equals(scope, "Side", StringComparison.OrdinalIgnoreCase) => ContainerRole.Side,
            _ => ContainerRole.Other,
        };
}
