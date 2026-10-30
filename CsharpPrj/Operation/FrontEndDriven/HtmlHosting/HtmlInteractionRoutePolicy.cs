#nullable enable

using System;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 交互端口/路由策略（通用件）—— 把「组合根解析 ECS 事件端口」与「环境变量灰度」两处隐式全局收敛为
/// **单一可注入策略点**：框架机制只读策略输出，不再在机制方法体里直呼服务定位器或读环境变量（可测、可注入）。
/// </summary>
public sealed class HtmlInteractionRoutePolicy
{
    /// <summary>路由灰度环境变量名（唯一字面量）。</summary>
    public const string RouteEnvironmentVariable = "WF_HTML_INTERACTION_ROUTE";

    private readonly Func<IHtmlInteractionPort?> _portProvider;
    private readonly Func<string?> _routeSetting;

    /// <summary>生产默认策略：端口取自组合根；路由取自环境变量（未声明 ⇒ 端口可得即 bus）。</summary>
    public static HtmlInteractionRoutePolicy Default { get; } = new();

    /// <param name="portProvider">交互端口提供者（null ⇒ 组合根解析 <see cref="IInteractionEventPort"/> 并包装）。</param>
    /// <param name="routeSetting">路由声明读取（null ⇒ 读 <see cref="RouteEnvironmentVariable"/>）。</param>
    public HtmlInteractionRoutePolicy(
        Func<IHtmlInteractionPort?>? portProvider = null,
        Func<string?>? routeSetting = null)
    {
        _portProvider = portProvider ?? DefaultPortProvider;
        _routeSetting = routeSetting ?? (static () => Environment.GetEnvironmentVariable(RouteEnvironmentVariable));
    }

    /// <summary>A2 交互端口：ECS 事件端口 → Plate 本地端口；未装配 ⇒ null（回退旧直调）。</summary>
    public IHtmlInteractionPort? ResolvePort() => _portProvider();

    /// <summary>
    /// A2 交互路由灰度：显式声明 <c>legacy|bus</c> 优先；
    /// 未显式声明时「端口可得 ⇒ bus」——使声明式链路默认生效，回退只需设 legacy。
    /// </summary>
    public HtmlInteractionRoute ResolveRoute()
    {
        var configured = _routeSetting();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().Equals("legacy", StringComparison.OrdinalIgnoreCase)
                ? HtmlInteractionRoute.Legacy
                : HtmlInteractionRoute.Bus;
        }

        return ResolvePort() is null ? HtmlInteractionRoute.Legacy : HtmlInteractionRoute.Bus;
    }

    private static IHtmlInteractionPort? DefaultPortProvider()
    {
        var port = AppServiceProvider.Resolve<IInteractionEventPort>();
        return port is null ? null : new HtmlInteractionAdapter(port);
    }
}
