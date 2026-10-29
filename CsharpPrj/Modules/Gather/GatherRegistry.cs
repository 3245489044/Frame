#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.Gather;

/// <summary>
/// 聚拢注册表（**loc 反转的装配点**）：把「声明」与「实现」在装配期闭合，运行期只按 location 解析。
/// <para>
/// 判据（装配期，任一不满足即 <see cref="Issues"/> 非空 ⇒ fail-closed）：
/// R1 声明合格性（<see cref="GatherSchema.Validate"/>）；R2 同一 location 只允许 1 个实现；
/// R3 每个 Need 恰有 1 个 Function；R4 每个 Need 都有实现；R5/R6 禁孤儿实现/孤儿函数；
/// R7 Produce 唯一；R8 Stage.deps 指向已声明 Stage。
/// </para>
/// </summary>
public sealed class GatherRegistry
{
    private readonly Dictionary<string, GatherProducer> _byLocation;
    private readonly Dictionary<string, string> _locationByDomain;

    private GatherRegistry(
        IReadOnlyList<GatherDecl> declarations,
        Dictionary<string, GatherProducer> byLocation,
        Dictionary<string, string> locationByDomain,
        IReadOnlyList<string> issues)
    {
        Declarations = declarations;
        _byLocation = byLocation;
        _locationByDomain = locationByDomain;
        Issues = issues;
    }

    /// <summary>装配后的完整声明集（审计/门禁复算）。</summary>
    public IReadOnlyList<GatherDecl> Declarations { get; }

    /// <summary>装配期问题清单（空 = 闭合）。</summary>
    public IReadOnlyList<string> Issues { get; }

    /// <summary>是否闭合（可运行）。</summary>
    public bool IsClosed => Issues.Count == 0;

    /// <summary>装配（多模块合并 + 闭包校验）。宿主/组合根唯一调用点。</summary>
    public static GatherRegistry Build(params IGatherModule[] modules)
        => Build((IEnumerable<IGatherModule>)modules, null);

    /// <summary>装配（多模块合并 + 闭包校验 + 与视图声明对账）。</summary>
    public static GatherRegistry Build(IEnumerable<IGatherModule> modules, GatherViewSpec? view)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var declarations = new List<GatherDecl>();
        var providers = new Dictionary<string, GatherProducer>(StringComparer.Ordinal);
        var issues = new List<string>();

        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            var builder = new GatherModuleBuilder();
            module.Declare(builder);

            declarations.Add(new GatherDecl(GatherDeclKind.Class, module.Name, GatherSchema.ModuleInterface));
            declarations.AddRange(builder.Declarations);

            foreach (var pair in builder.Providers)
            {
                if (!providers.TryAdd(pair.Key, pair.Value))
                {
                    issues.Add($"[R2] location '{pair.Key}' 有多个实现（违反 SingleImplementation）");
                }
            }
        }

        issues.AddRange(GatherSchema.Validate(declarations));

        var needLocations = new HashSet<string>(StringComparer.Ordinal);
        var locationByDomain = new Dictionary<string, string>(StringComparer.Ordinal);
        var produced = new HashSet<string>(StringComparer.Ordinal);
        var stageNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var decl in declarations)
        {
            switch (decl.Kind)
            {
                case GatherDeclKind.Need:
                    needLocations.Add(decl.Location ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(decl.Location))
                    {
                        locationByDomain[decl.Name] = decl.Location!;
                    }

                    break;
                case GatherDeclKind.Produce:
                    if (!produced.Add(decl.Name))
                    {
                        issues.Add($"[R7] Produce 重名：'{decl.Name}'");
                    }

                    break;
                case GatherDeclKind.Stage:
                    stageNames.Add(decl.Name);
                    break;
            }
        }

        foreach (var location in needLocations)
        {
            var functionCount = 0;
            foreach (var decl in declarations)
            {
                if (decl.Kind == GatherDeclKind.Function &&
                    string.Equals(decl.Location, location, StringComparison.Ordinal))
                {
                    functionCount++;
                }
            }

            if (functionCount != 1)
            {
                issues.Add($"[R3] location '{location}' 的 Function 数 = {functionCount}（须恰 1）");
            }

            if (!providers.ContainsKey(location))
            {
                issues.Add($"[R4] location '{location}' 无实现（声明了 Need 但无人提供）");
            }
        }

        foreach (var location in providers.Keys)
        {
            if (!needLocations.Contains(location))
            {
                issues.Add($"[R5] 孤儿实现：location '{location}' 未被任何 Need 声明");
            }
        }

        foreach (var decl in declarations)
        {
            if (decl.Kind == GatherDeclKind.Function &&
                !needLocations.Contains(decl.Location ?? string.Empty))
            {
                issues.Add($"[R6] 孤儿函数：'{decl.Name}' 绑定的 location '{decl.Location}' 未声明 Need");
            }
        }

        foreach (var decl in declarations)
        {
            if (decl.Kind != GatherDeclKind.Stage || decl.Deps is null)
            {
                continue;
            }

            foreach (var dep in decl.Deps)
            {
                if (!stageNames.Contains(dep))
                {
                    issues.Add($"[R8] Stage '{decl.Name}' 依赖未声明 Stage '{dep}'");
                }
            }
        }

        // R9/R10：声明面（视图 JSON）与实现面（模块 Declare）双向对账 —— 防「声明了视图要 X，代码却没提供」。
        if (view is not null)
        {
            var needDomains = new HashSet<string>(StringComparer.Ordinal);
            var produceNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var decl in declarations)
            {
                if (decl.Kind == GatherDeclKind.Need)
                {
                    needDomains.Add(decl.Name);
                }
                else if (decl.Kind == GatherDeclKind.Produce)
                {
                    produceNames.Add(decl.Name);
                }
            }

            foreach (var need in view.Needs)
            {
                if (!needDomains.Contains(need.Domain))
                {
                    issues.Add($"[R9] 视图 '{view.View}' 的 need '{need.Domain}' 未在模块声明（视图与实现不闭合）");
                }
            }

            foreach (var produce in view.Produces)
            {
                if (!produceNames.Contains(produce.Name))
                {
                    issues.Add($"[R10] 视图 '{view.View}' 的 produce '{produce.Name}' 未在模块声明（出口未闭合）");
                }
            }
        }

        return new GatherRegistry(declarations, providers, locationByDomain, issues);
    }

    /// <summary>域 → 提供该域的 location（loc 反转：调用方只认域，装配面决定落点）。</summary>
    public string LocationOf(string domain)
        => _locationByDomain.TryGetValue(domain, out var location)
            ? location
            : throw new GatherClosureViolationException($"域 '{domain}' 未声明 Need（先 Need → Function → Provide 闭合）");

    /// <summary>按 location 解析实现（未登记 ⇒ 抛；装配期已保证闭合）。</summary>
    public GatherProducer Producer(string location)
        => _byLocation.TryGetValue(location, out var producer)
            ? producer
            : throw new GatherClosureViolationException(
                $"location '{location}' 无实现（聚拢未闭合；先声明 Need/Function/Provide）");

    /// <summary>尝试解析实现。</summary>
    public bool TryProducer(string location, out GatherProducer? producer)
        => _byLocation.TryGetValue(location, out producer);
}
