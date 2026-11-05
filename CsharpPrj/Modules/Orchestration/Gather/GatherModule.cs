#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Gather;

/// <summary>取数函数（**唯一签名**；与 <see cref="GatherSchema.ProducerSignature"/> 一致）。</summary>
public delegate Task<object?> GatherProducer(GatherNeedSpec need, CancellationToken ct);

/// <summary>
/// 聚拢模块（loc 反转的宿主侧唯一入口）：宿主只**声明**自己提供哪些 location 与实现，
/// 不直接调用框架内部；装配由 <see cref="GatherRegistry"/> 闭合。
/// </summary>
public interface IGatherModule
{
    /// <summary>模块名（同时作为 Class 声明名，受 <see cref="GatherSchema.NamePattern"/> 约束）。</summary>
    string Name { get; }

    /// <summary>在装配期声明：Class / Variable / Need / Function / Produce + 对应实现。</summary>
    void Declare(GatherModuleBuilder builder);
}

/// <summary>
/// 模块声明构造器（**唯一声明面**）：名字、签名、绑定关系只能经这里登记。
/// <para>纪律：<c>Need</c> 必须同时有 <c>Function</c> 与 <c>Provide</c>（同 location），否则装配期红。</para>
/// </summary>
public sealed class GatherModuleBuilder
{
    private readonly List<GatherDecl> _declarations = new();
    private readonly Dictionary<string, GatherProducer> _providers = new(StringComparer.Ordinal);

    /// <summary>本模块声明的全部事实（供注册表校验）。</summary>
    public IReadOnlyList<GatherDecl> Declarations => _declarations;

    /// <summary>location → 实现（供注册表解析）。</summary>
    public IReadOnlyDictionary<string, GatherProducer> Providers => _providers;

    /// <summary>声明实现体（接口固定为 <see cref="GatherSchema.ModuleInterface"/>）。</summary>
    public GatherModuleBuilder Class(string name)
    {
        _declarations.Add(new GatherDecl(GatherDeclKind.Class, name, GatherSchema.ModuleInterface));
        return this;
    }

    /// <summary>声明框架持有变量（宿主禁自建同名状态）。</summary>
    public GatherModuleBuilder Variable(string name, string scope, string type)
    {
        _declarations.Add(new GatherDecl(GatherDeclKind.Variable, name, type, scope));
        return this;
    }

    /// <summary>声明阶段（顺序只由 deps 表达）。</summary>
    public GatherModuleBuilder Stage(string name, IReadOnlyList<string> dependsOn)
    {
        _declarations.Add(new GatherDecl(GatherDeclKind.Stage, name, Deps: dependsOn));
        return this;
    }

    /// <summary>声明需求：域 + 作用域 + **提供该域的 location** + 成本档 + 是否可复用。</summary>
    public GatherModuleBuilder Need(
        string domain,
        string scope,
        string location,
        GatherCostClass cost = GatherCostClass.Low,
        bool reusable = false)
    {
        _declarations.Add(new GatherDecl(
            GatherDeclKind.Need, domain, Location: location, Scope: scope));
        return this;
    }

    /// <summary>声明函数（签名固定；必须绑定 location）。</summary>
    public GatherModuleBuilder Function(string name, string location)
    {
        _declarations.Add(new GatherDecl(GatherDeclKind.Function, name, GatherSchema.ProducerSignature, location));
        return this;
    }

    /// <summary>登记 location 的实现（函数体）。</summary>
    public GatherModuleBuilder Provide(string location, GatherProducer producer)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new ArgumentException("location 不能为空", nameof(location));
        }

        _providers[location] = producer ?? throw new ArgumentNullException(nameof(producer));
        return this;
    }

    /// <summary>
    /// 按视图声明**派生** Need/Function/Stage/Produce（消除模块内硬编码视图声明）。
    /// <para>location 约定 = need.Domain（一个域一个取数实现点）；实现体由 <see cref="ProvideFor"/> 登记。</para>
    /// </summary>
    public GatherModuleBuilder FromView(GatherViewSpec view)
    {
        ArgumentNullException.ThrowIfNull(view);

        foreach (var stage in view.Stages)
        {
            Stage(stage.Name, stage.Deps);
        }

        foreach (var need in view.Needs)
        {
            Need(need.Domain, need.Scope, need.Domain, need.Cost, need.Reusable);
            Function(need.Domain, need.Domain);
        }

        foreach (var produce in view.Produces)
        {
            Produce(produce.Name, produce.Kind, produce.Retention, produce.Crit, produce.Recompute);
        }

        return this;
    }

    /// <summary>
    /// 为「视图确实需要的域」登记实现；视图未声明该 need ⇒ 不登记（禁孤儿实现 R6）。
    /// </summary>
    public GatherModuleBuilder ProvideFor(GatherViewSpec view, string domain, GatherProducer producer)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.FindNeed(domain) is null ? this : Provide(domain, producer);
    }

    /// <summary>声明产物（出口闭合的判据载体）。</summary>
    public GatherModuleBuilder Produce(
        string name,
        string kind,
        string retention,
        string crit,
        string? recompute = null)
    {
        _declarations.Add(new GatherDecl(
            GatherDeclKind.Produce, name, Type: kind, Location: retention, Crit: crit, Recompute: recompute));
        return this;
    }
}
