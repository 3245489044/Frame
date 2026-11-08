#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Modules.Gather;

/// <summary>
/// 受控声明种类（**闭集**）：聚拢框架只承认这六类声明，任何"顺手实现"都必须先落成声明。
/// <para>Need=要什么 / Stage=阶段 / Produce=产出 / Function=取数函数 / Class=实现体 / Variable=框架持有变量。</para>
/// </summary>
public enum GatherDeclKind
{
    Need,
    Stage,
    Produce,
    Function,
    Class,
    Variable,
}

/// <summary>
/// 一条受控声明（统一的声明事实载体）：<see cref="GatherDeclKind"/> 决定必填字段。
/// <para>约束（<see cref="GatherSchema"/>）：名字受控、Kind 必填字段齐备、同名同 Kind 禁重。</para>
/// </summary>
public sealed record GatherDecl(
    GatherDeclKind Kind,
    string Name,
    string? Type = null,
    string? Location = null,
    string? Scope = null,
    string? Crit = null,
    string? Recompute = null,
    IReadOnlyList<string>? Deps = null,
    GatherCostClass Cost = GatherCostClass.Low,
    bool Reusable = false);

/// <summary>
/// 声明 Schema（**集中约束**）：函数签名 / 类接口 / 变量形态 / 命名 / 必填项 只在这一处定义。
/// <para>装配期由 <see cref="GatherRegistry.Build"/> 调用；门禁 <c>Check-GatherDecl</c> 复用同一口径。</para>
/// </summary>
public static class GatherSchema
{
    /// <summary>受控命名：小写起，只允许 <c>a-z0-9._-</c>（禁空格/中文/大写，防同一事实两种写法）。</summary>
    public const string NamePattern = "^[a-z][a-z0-9._-]*$";

    /// <summary>取数函数的**唯一签名**（宿主不得自定义其它形态）。</summary>
    public const string ProducerSignature = "Func<GatherNeedSpec,CancellationToken,Task<object?>>";

    /// <summary>实现体的**唯一接口**（loc 反转的绑定面）。</summary>
    public const string ModuleInterface = "IGatherModule";

    /// <summary>框架持有的变量（宿主禁自建同名状态）——快照缓存与变更作用域由框架单点持有。</summary>
    public static readonly string[] FrameworkVariables = { "snapshot-cache", "changed-scopes" };

    private static readonly Regex NameRegex = new(NamePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>受控命名判定（**单源**）：实现侧声明与视图声明数据共用同一判据（防两侧同错即通过）。</summary>
    public static bool IsValidName(string? name)
        => !string.IsNullOrWhiteSpace(name) && NameRegex.IsMatch(name);

    /// <summary>声明合格性（纯函数；空 = 合格）。</summary>
    public static IReadOnlyList<string> Validate(IEnumerable<GatherDecl> declarations)
    {
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var decl in declarations)
        {
            if (!IsValidName(decl.Name))
            {
                issues.Add($"[D1] 声明名非法：'{decl.Name}'（须匹配 {NamePattern}）");
                continue;
            }

            var identity = $"{decl.Kind}:{decl.Name}";
            if (!seen.Add(identity))
            {
                issues.Add($"[D2] 声明重复：{identity}");
            }

            switch (decl.Kind)
            {
                case GatherDeclKind.Need:
                    if (string.IsNullOrWhiteSpace(decl.Location))
                    {
                        issues.Add($"[D3] Need '{decl.Name}' 缺 location（声明了需求却不说由哪个实现体提供）");
                    }

                    if (string.IsNullOrWhiteSpace(decl.Scope))
                    {
                        issues.Add($"[D3] Need '{decl.Name}' 缺 scope（复用键须含作用域）");
                    }

                    break;

                case GatherDeclKind.Stage:
                    if (decl.Deps is null)
                    {
                        issues.Add($"[D4] Stage '{decl.Name}' 缺 deps（顺序只能由依赖边表达）");
                    }

                    break;

                case GatherDeclKind.Produce:
                    if (decl.Crit is not ("micro" or "macro"))
                    {
                        issues.Add($"[D5] Produce '{decl.Name}' 的 crit='{decl.Crit}' 非法（∈ micro|macro）");
                    }

                    if (decl.Crit == "macro" && string.IsNullOrWhiteSpace(decl.Recompute))
                    {
                        issues.Add($"[D5] Produce '{decl.Name}' 为 crit:macro 但缺 recompute");
                    }

                    break;

                case GatherDeclKind.Function:
                    if (!string.IsNullOrWhiteSpace(decl.Type) &&
                        !string.Equals(decl.Type, ProducerSignature, StringComparison.Ordinal))
                    {
                        issues.Add($"[D6] Function '{decl.Name}' 签名非法：'{decl.Type}'（唯一允许 = {ProducerSignature}）");
                    }

                    if (string.IsNullOrWhiteSpace(decl.Location))
                    {
                        issues.Add($"[D6] Function '{decl.Name}' 缺 location（函数必须绑定到某 Need）");
                    }

                    break;

                case GatherDeclKind.Class:
                    if (!string.IsNullOrWhiteSpace(decl.Type) &&
                        !string.Equals(decl.Type, ModuleInterface, StringComparison.Ordinal))
                    {
                        issues.Add($"[D7] Class '{decl.Name}' 接口非法：'{decl.Type}'（唯一允许 = {ModuleInterface}）");
                    }

                    break;

                case GatherDeclKind.Variable:
                    if (string.IsNullOrWhiteSpace(decl.Type) || string.IsNullOrWhiteSpace(decl.Location))
                    {
                        issues.Add($"[D8] Variable '{decl.Name}' 缺 type/location");
                    }

                    break;

                default:
                    issues.Add($"[D9] 未知声明种类：{decl.Kind}");
                    break;
            }
        }

        return issues;
    }
}
