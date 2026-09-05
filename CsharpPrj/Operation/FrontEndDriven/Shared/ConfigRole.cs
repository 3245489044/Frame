#nullable enable

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
        // boot 宿主根声明（如 MainLayout.json boot[].host="root"）
        if (config.Boot is { Count: > 0 })
        {
            foreach (var phase in config.Boot)
            {
                if (!string.IsNullOrWhiteSpace(phase.Host))
                    return true;
            }
        }

        if (config.Views is { Count: > 0 })
        {
            foreach (var view in config.Views)
            {
                if (view.Controls is { Count: > 0 } && WalkHost(view.Controls))
                    return true;
            }
        }

        return false;
    }

    private static bool WalkHost(List<ControlConfig> controls)
    {
        if (controls == null) return false;

        foreach (var c in controls)
        {
            if (c == null) continue;

            // 宿主节点：声明了子作用域的 container
            if (c.Container is { StateScope: not null and not "" })
                return true;

            if (c.Controls is { Count: > 0 } && WalkHost(c.Controls)) return true;
            if (c.Panel1Controls is { Count: > 0 } && WalkHost(c.Panel1Controls)) return true;
            if (c.Panel2Controls is { Count: > 0 } && WalkHost(c.Panel2Controls)) return true;

            if (c.Tabs is { Count: > 0 })
            {
                foreach (var tab in c.Tabs)
                {
                    if (tab?.Controls is { Count: > 0 } && WalkHost(tab.Controls)) return true;
                }
            }

            if (c.HtmlPage?.Controls is { Count: > 0 } && WalkHost(c.HtmlPage.Controls)) return true;
        }

        return false;
    }
}
