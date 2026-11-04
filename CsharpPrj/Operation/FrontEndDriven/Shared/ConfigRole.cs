#nullable enable

using System;
using System.Collections.Generic;
using TConfig;

namespace FrontEndDriven.Shared;

/// <summary>
/// 配置角色（F2·「主布局壳误载内容循环注入框架修复方案」）：Shell | Content 显式建模。
/// 角色为派生判定（读取已反序列化的 UIConfiguration 控件树，不新增第二次 JSON 解析），
/// 非 JSON 必填字段——消灭"人人假设渲染目标是 Content"的隐性契约，让角色成为框架一等公民。
/// </summary>
public enum ConfigRole
{
    /// <summary>内容配置：控件树无任何宿主节点（如 ConfigSelector / MathFunctions / WorkflowInputUI）。</summary>
    Content,

    /// <summary>壳配置：控件树存在承载子作用域的宿主节点（container.stateScope / splitContainer 面板内嵌宿主 / boot 宿主根）。例：MainLayout。</summary>
    Shell
}

/// <summary>
/// 配置角色解析（纯逻辑，可单测）：递归扫描控件树是否存在宿主节点。
/// 宿主节点判定 = 任意 ControlConfig.Container.StateScope 非空 ∨ boot 声明了 host 宿主根。
/// </summary>
public static class ConfigRoleResolver
{
    /// <summary>派生角色：控件树含宿主节点 → Shell；否则 Content。null 配置视为 Content（无可承载子作用域）。</summary>
    public static ConfigRole GetRole(UIConfiguration? config)
        => config != null && HasHostNode(config) ? ConfigRole.Shell : ConfigRole.Content;

    private static bool HasHostNode(UIConfiguration config)
    {
        var declarations = CollectDeclarations(config);
        // 任一 StateScope 已足以判定壳（此前仅看宿主 name，StateScope 无 name 时会误判 Content）。
        return declarations.HostNames.Count > 0 || declarations.Scopes.Count > 0;
    }

    /// <summary>
    /// 唯一宿主采集器（E-17）：ConfigRole 与 RenderHostCycleGuard 共用，消除两套 walker 与宿主定义漂移。
    /// 采集口径：boot[].host 宿主根、任意 Container.StateScope 非空节点（scope + name）、
    /// container/splitContainer 节点 name；递归覆盖 Controls/Panel1/Panel2/Tabs/HtmlPage。
    /// </summary>
    public static (HashSet<string> Scopes, HashSet<string> HostNames) CollectDeclarations(UIConfiguration? config)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hostNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config is null) return (scopes, hostNames);

        // boot 宿主根声明（如 MainLayout.json boot[].host="root"）——此前 Guard 未采集，导致 boot-only 壳绕过拦截。
        if (config.Boot is { Count: > 0 })
        {
            foreach (var phase in config.Boot)
            {
                if (!string.IsNullOrWhiteSpace(phase.Host))
                    hostNames.Add(phase.Host);
            }
        }

        if (config.Views is { Count: > 0 })
        {
            foreach (var view in config.Views)
            {
                if (view.Controls is { Count: > 0 })
                    WalkCollect(view.Controls, scopes, hostNames);
            }
        }

        return (scopes, hostNames);
    }

    private static void WalkCollect(List<ControlConfig>? controls, HashSet<string> scopes, HashSet<string> hostNames)
    {
        if (controls == null) return;

        foreach (var c in controls)
        {
            if (c == null) continue;

            if (c.Container is { StateScope: not null and not "" })
            {
                scopes.Add(c.Container.StateScope);
                if (!string.IsNullOrWhiteSpace(c.Name))
                    hostNames.Add(c.Name);
            }
            else if (!string.IsNullOrWhiteSpace(c.Name) && IsHostCapable(c))
            {
                hostNames.Add(c.Name);
            }

            WalkCollect(c.Controls, scopes, hostNames);
            WalkCollect(c.Panel1Controls, scopes, hostNames);
            WalkCollect(c.Panel2Controls, scopes, hostNames);

            if (c.Tabs is { Count: > 0 })
            {
                foreach (var tab in c.Tabs)
                    WalkCollect(tab?.Controls, scopes, hostNames);
            }

            WalkCollect(c.HtmlPage?.Controls, scopes, hostNames);
        }
    }

    private static bool IsHostCapable(ControlConfig c)
        => string.Equals(c.Type, "container", StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Type, "splitContainer", StringComparison.OrdinalIgnoreCase);
}
