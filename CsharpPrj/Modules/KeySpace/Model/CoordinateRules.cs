using System;
using System.Collections.Generic;

namespace Modules.KeySpace.Model;

/// <summary>坐标归一口径（声明驱动；禁在消费点手写等价分支）。</summary>
public enum CoordinateRuleKind
{
    /// <summary>原样作为规范值（裸业务键 / 已规范形态）。</summary>
    Identity,

    /// <summary>剥离声明前缀；剥离后为空 ⇒ 未命中。</summary>
    StripPrefix,

    /// <summary>按目标族文法校验（不通过 ⇒ 未命中）。</summary>
    RequireFamily,
}

/// <summary>未命中姿态：<see cref="Fail"/> = 返回不可解析（禁回退原值）；<see cref="Keep"/> = 显式放行原值。</summary>
public enum CoordinateMissPolicy
{
    Fail,
    Keep,
}

/// <summary>一条坐标规则：<c>from=&lt;前缀|*&gt; | rule=… | on-miss=…</c>。</summary>
/// <param name="From">匹配前缀（<c>*</c> = 其它全部）。</param>
/// <param name="Rule">归一口径。</param>
/// <param name="OnMiss">未命中姿态。</param>
/// <param name="Family">目标族（<see cref="CoordinateRuleKind.RequireFamily"/> 用）。</param>
public sealed record CoordinateRule(
    string From,
    CoordinateRuleKind Rule,
    CoordinateMissPolicy OnMiss,
    string? Family = null);

/// <summary>坐标规则集（族/轴级；声明事实，运行期只读）。</summary>
/// <param name="Name">规则集名（轴 <c>canonical=</c> 的引用目标）。</param>
/// <param name="FamilyId">所属族。</param>
/// <param name="Axis">所属轴。</param>
/// <param name="Idempotent">是否声明幂等（Resolve∘Resolve = Resolve）。</param>
/// <param name="Rules">规则（声明序；匹配取最长前缀，其次 <c>*</c>）。</param>
public sealed record CoordinateRuleSet(
    string Name,
    string FamilyId,
    string Axis,
    bool Idempotent,
    IReadOnlyList<CoordinateRule> Rules);

/// <summary>坐标解析结果（三态语义：Ok=false 时 Reason 必非空；禁静默回退）。</summary>
/// <param name="Ok">是否解析出规范值。</param>
/// <param name="Value">规范值（Ok=false 时为空串）。</param>
/// <param name="Reason">原因码（ok / unresolved-empty / unresolved-unknown-coordinate / unresolved-noncanonical）。</param>
public readonly record struct CoordinateResult(bool Ok, string Value, string Reason)
{
    /// <summary>解析成功。</summary>
    public static CoordinateResult Resolved(string value) => new(true, value, "ok");

    /// <summary>解析失败（原因码必填）。</summary>
    public static CoordinateResult Unresolved(string reason)
        => new(false, string.Empty, string.IsNullOrWhiteSpace(reason) ? "unresolved" : reason);
}
