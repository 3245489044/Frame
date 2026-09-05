#nullable enable

using System;
using ECS.Core;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS.Components;

namespace FrontEndDriven.ControlLookup;

/// <summary>
/// 活动作用域上下文（Phase A）：AppServiceProvider 注册的单例，记录"命令当前应作用的渲染 scope"。
/// 由脚本 load-config-show / load 命令在加载配置时置为目标 scope；命令寻址据此优先在活动 scope 内解析。
/// </summary>
public sealed class ScopeContext : IScopeContext
{
    private string? _currentScope;

    public string? CurrentScope
    {
        get => _currentScope;
        set => _currentScope = string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>
/// 作用域感知实体解析（Phase A·P6 基础）：命令按名解析实体时，优先在活动 scope 内查找；
/// 活动 scope 未命中则回退全局首匹配（**保持旧行为不回归**），并显式记录"跨作用域回退"诊断，
/// 让「脚本点 ClineBrowser.clearButton 误中 MainLayout.clearButton」这类串扰变得可见、可审计。
/// 消费方：EcsStateAccessor / EcsEventBus 的 FindEntityByName（命令 get/set/click 的统一寻址点）。
/// </summary>
public static class ScopeAwareResolver
{
    /// <summary>
    /// 作用域感知解析：活动 scope 命中 → 返回；活动 scope 未命中 → 回退全局首匹配并记录跨作用域回退诊断。
    /// scopeContext 为 null（未装配）时退化为无 scope 过滤的全局首匹配（旧行为）。
    /// </summary>
    public static Entity? ResolveEntity(EntityQueryService query, IScopeContext? scopeContext, string controlName)
    {
        if (query == null || string.IsNullOrWhiteSpace(controlName))
            return null;

        var active = scopeContext?.CurrentScope;
        if (string.IsNullOrWhiteSpace(active))
            return query.FindByName(controlName);

        var inScope = query.FindByName(controlName, active);
        if (inScope != null)
            return inScope;

        // 活动 scope 未命中 → 回退全局首匹配，并诊断目标实际命中的 scope（串扰可见化）。
        var global = query.FindByName(controlName);
        if (global != null)
        {
            var hitScope = query.EntityManager.TryGetComponent<ScopeComponent>(global.Value, out var sc)
                ? sc.Scope
                : "(未知)";
            FrontEndDrivenLog.Log("ScopeAwareResolver_cross_scope_fallback",
                new { controlName, activeScope = active, hitScope });
        }

        return global;
    }
}
