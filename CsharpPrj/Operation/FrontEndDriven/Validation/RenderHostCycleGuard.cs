#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using FrameworkAbstractions.ContainerAbstractions;
using FrontEndDriven.Shared;
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
        // E-17：与 ConfigRole 共用同一采集器（含 boot[].host），消除两套 walker 与宿主定义漂移。
        if (config is null)
            return RenderHostCycleResult.Fail(Array.Empty<string>(),
                "配置为空：无任何宿主声明，拒绝渲染以免无法证明非循环注入。");

        var (declaredScopes, hostControlNames) = ConfigRoleResolver.CollectDeclarations(config);

        var cSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(renderScope)) cSet.Add(renderScope);
        if (host?.Id != null)
        {
            if (!string.IsNullOrWhiteSpace(host.Id.ScopeId)) cSet.Add(host.Id.ScopeId);
            if (!string.IsNullOrWhiteSpace(host.Id.HostKey)) cSet.Add(host.Id.HostKey);
        }

        // 自身根宿主豁免：boot[].host 是壳自己的根宿主，当渲染目标恰为该 boot 阶段声明的 scope 时
        // （合法根壳渲染），它不构成「被注入的子宿主」。缺此豁免会让
        // MainLayout(boot.host=root) 渲染进 scope=MainLayout(hostKey=root) 自撞拦截 → 首屏空白。
        var selfBootHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config.Boot is { Count: > 0 })
        {
            foreach (var phase in config.Boot)
            {
                if (!string.IsNullOrWhiteSpace(phase.Host) &&
                    !string.IsNullOrWhiteSpace(phase.Scope) &&
                    string.Equals(phase.Scope, renderScope, StringComparison.OrdinalIgnoreCase))
                {
                    selfBootHosts.Add(phase.Host);
                }
            }
        }

        var hits = new List<string>();
        foreach (var scope in declaredScopes)
        {
            if (cSet.Contains(scope))
                hits.Add($"scope:{scope}");
        }
        foreach (var name in hostControlNames)
        {
            if (selfBootHosts.Contains(name)) continue;
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

}
