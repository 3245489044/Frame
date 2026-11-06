using System;
using System.Collections.Generic;
using ContractFramework.Codes;

namespace Modules.Parsing.Invocation;

/// <summary>调用面违约类别（机读；命令层/门禁按类别聚合，不解析中文）。</summary>
public enum InvocationIssueKind
{
    /// <summary>命令动作未登记声明（R1）。</summary>
    UnknownAction = 0,

    /// <summary>目标串 token 数不足（R1，语法未闭合）。</summary>
    TargetIncomplete = 1,

    /// <summary>参数键不在声明面（R2）。</summary>
    ArgumentNotDeclared = 2,

    /// <summary>参数值非法（R2：非数值 timeout / 非法计数表达式等）。</summary>
    ArgumentValueInvalid = 3,

    /// <summary>目标名不在其所属命名空间（R7）。</summary>
    NameNotInSpace = 4,

    /// <summary>该命名空间无来源（该校验维度未参与；不是「不存在」）。</summary>
    NameSpaceUnavailable = 5,
}

/// <summary>受控原因码（唯一码表；门禁与账本按码聚合）。</summary>
public static class InvocationReasonCodes
{
    public const string ActionNotDeclared = "R1-action-not-declared";
    public const string TargetIncomplete = "R1-target-incomplete";
    public const string ArgumentNotDeclared = "R2-argument-not-declared";
    public const string ArgumentValueInvalid = "R2-argument-value-invalid";
    public const string NameNotInSpace = "R7-name-not-in-space";
    public const string SpaceUnavailable = "R7-space-unavailable";

    /// <summary>码表（唯一实现；缺失码 ⇒ 门禁判红）。</summary>
    public static readonly CodeSet All = new(
        "invocation",
        ActionNotDeclared,
        TargetIncomplete,
        ArgumentNotDeclared,
        ArgumentValueInvalid,
        NameNotInSpace,
        SpaceUnavailable);

    public static string For(InvocationIssueKind kind)
        => kind switch
        {
            InvocationIssueKind.UnknownAction => ActionNotDeclared,
            InvocationIssueKind.TargetIncomplete => TargetIncomplete,
            InvocationIssueKind.ArgumentNotDeclared => ArgumentNotDeclared,
            InvocationIssueKind.ArgumentValueInvalid => ArgumentValueInvalid,
            InvocationIssueKind.NameNotInSpace => NameNotInSpace,
            InvocationIssueKind.NameSpaceUnavailable => SpaceUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未登记的违约类别。"),
        };
}

/// <summary>一条调用面违约（结构事实：下标/动作/名字/空间/原因码）。</summary>
public sealed record InvocationIssue(
    InvocationIssueKind Kind,
    int Index,
    string Action,
    string? SubAction,
    string? Name,
    NameSpaceId Space,
    string Message)
{
    /// <summary>受控原因码（由类别唯一派生）。</summary>
    public string ReasonCode => InvocationReasonCodes.For(Kind);

    public override string ToString()
        => $"#{Index} {Action}{(SubAction is null ? string.Empty : " " + SubAction)} [{Space}] {ReasonCode} {Message}";
}

/// <summary>调用面校验报告（Checked=参与校验条目数；NameChecks=实际执行了命名空间校验的次数）。</summary>
public sealed record InvocationContractReport(
    IReadOnlyList<InvocationIssue> Issues,
    int Checked,
    int NameChecks,
    bool Blocking)
{
    public static InvocationContractReport Empty { get; } = new([], 0, 0, false);

    public bool IsClean => Issues.Count == 0;

    /// <summary>放行判定：无违约，或本批只记账（shadow/off）。</summary>
    public bool Admits => IsClean || !Blocking;

    public string Summary(int limit = 8)
    {
        var lines = new List<string>(Math.Min(limit, Issues.Count));
        for (var i = 0; i < Issues.Count && i < limit; i++)
            lines.Add(Issues[i].ToString());
        return string.Join(" | ", lines);
    }
}
