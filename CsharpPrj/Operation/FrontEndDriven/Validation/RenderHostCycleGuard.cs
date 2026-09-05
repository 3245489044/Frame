#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using FrameworkAbstractions.ContainerAbstractions;
using TConfig;

namespace FrontEndDriven.Validation;

/// <summary>
/// 渲染宿主循环防护结果（F1·「主布局壳误载内容循环注入框架修复方案」）。
/// </summary>
public sealed record RenderHostCycleResult(bool Allowed, IReadOnlyList<string> Hits, string? Error)
{
    public static RenderHostCycleResult Pass() => new(true, Array.Empty<string>(), null);

    public static RenderHostCycleResult Fail(IReadOnlyList<string> hits, string error)
        => new(false, hits, error);
}

/// <summary>
/// 渲染宿主循环依赖防护（F1·P0 核心）——状态管线渲染段唯一权威闸门，覆盖
/// boot / UIToolbox / 脚本 load / 生命周期钩子所有调用方。
/// 判定：待渲染配置声明了它正在渲染进入的那个作用域（declaredScopes ∋ renderScope），
///   ∨ 配置控件树含宿主控件 name 命中渲染目标 → 拒绝渲染，阻断"壳误载为内容"的循环宿主注入。
/// 纯逻辑、可单测；采集走已反序列化的 UIConfiguration 控件树（不新增第二次解析）。
/// </summary>
public static class RenderHostCycleGuard
{
    /// <summary>
    /// 判定算法（输入：待渲染配置 config、目标作用域 renderScope、宿主 host）：
    ///   A = config 控件树中所有 container 节点的 stateScope 引用（递归采集）
    ///   B = config 控件树中出现的宿主控件 name（container/splitContainer 节点 name）
    ///   C = { renderScope, host?.ScopeId, host?.HostKey }
    ///   循环条件：A ∩ C ≠ ∅  OR  B ∩ C ≠ ∅
    /// </summary>
    public static RenderHostCycleResult Validate(UIConfiguration? config, string? renderScope, ContainerHandle? host)
    {
        var declaredScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hostControlNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config != null)
            Collect(config, declaredScopes, hostControlNames);

        var cSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(renderScope)) cSet.Add(renderScope);
        if (host?.Id != null)
        {
            if (!string.IsNullOrWhiteSpace(host.Id.ScopeId)) cSet.Add(host.Id.ScopeId);
            if (!string.IsNullOrWhiteSpace(host.Id.HostKey)) cSet.Add(host.Id.HostKey);
        }

        var hits = new List<string>();
        foreach (var scope in declaredScopes)
        {
            if (cSet.Contains(scope))
                hits.Add($"scope:{scope}");
        }
        foreach (var name in hostControlNames)
        {
            if (cSet.Contains(name))
                hits.Add($"host:{name}");
        }

        if (hits.Count > 0)
        {
            var error = new StringBuilder()
                .Append("配置为宿主壳：声明了宿主作用域/宿主控件 {")
                .Append(string.Join(", ", hits))
                .Append("}，其中含渲染目标作用域 ")
                .Append(string.IsNullOrWhiteSpace(renderScope) ? "(空)" : renderScope)
                .Append("。壳配置只能作为根壳（boot / 其自身声明的作用域）渲染，"
                    + "不能作为内容注入其自声明的宿主 → 循环宿主注入已拦截。")
                .ToString();
            return RenderHostCycleResult.Fail(hits, error);
        }

        return RenderHostCycleResult.Pass();
    }

    private static void Collect(UIConfiguration config, HashSet<string> scopes, HashSet<string> hostNames)
    {
        if (config.Views is { Count: > 0 })
        {
            foreach (var view in config.Views)
            {
                if (view.Controls is { Count: > 0 })
                    Walk(view.Controls, scopes, hostNames);
            }
        }
    }

    private static void Walk(List<ControlConfig> controls, HashSet<string> scopes, HashSet<string> hostNames)
    {
        if (controls == null) return;

        foreach (var c in controls)
        {
            if (c == null) continue;

            // 宿主容器：声明了子作用域 → A 集收录 StateScope、B 集收录其 name
            if (c.Container is { StateScope: not null and not "" })
            {
                scopes.Add(c.Container.StateScope);
                if (!string.IsNullOrWhiteSpace(c.Name))
                    hostNames.Add(c.Name);
            }
            // container/splitContainer 节点本身即宿主控件 name 候选 → B 集收录
            else if (!string.IsNullOrWhiteSpace(c.Name) && IsHostCapable(c))
            {
                hostNames.Add(c.Name);
            }

            Walk(c.Controls, scopes, hostNames);
            Walk(c.Panel1Controls, scopes, hostNames);
            Walk(c.Panel2Controls, scopes, hostNames);

            if (c.Tabs is { Count: > 0 })
            {
                foreach (var tab in c.Tabs)
                {
                    if (tab?.Controls is { Count: > 0 })
                        Walk(tab.Controls, scopes, hostNames);
                }
            }

            if (c.HtmlPage?.Controls is { Count: > 0 })
                Walk(c.HtmlPage.Controls, scopes, hostNames);
        }
    }

    private static bool IsHostCapable(ControlConfig c)
        => string.Equals(c.Type, "container", StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Type, "splitContainer", StringComparison.OrdinalIgnoreCase);
}
