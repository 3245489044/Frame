using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Parsing.Models;

namespace Modules.Parsing.Invocation;

/// <summary>一个名字归属声明（目标名或具名参数值）。</summary>
public readonly record struct NameClaim(NameSpaceId Space, string Name);

/// <summary>绑定结果：命令 + 声明 + 名字归属（供校验器与命令层共用同一份解释）。</summary>
public sealed record BoundInvocation(
    string Action,
    string? SubAction,
    NameSpaceId TargetSpace,
    string? TargetName,
    IReadOnlyList<string> Tokens,
    IReadOnlyDictionary<string, object?> Args,
    IReadOnlyList<NameClaim> NameClaims);

/// <summary>
/// 调用绑定（唯一解释器）：声明存在 ∧ 目标 token 闭合 ∧ 参数∈声明。
/// 命令层不再自行切分目标串或判定参数键合法性。
/// </summary>
public static class InvocationBinding
{
    private static readonly IReadOnlyDictionary<string, object?> NoArgs = new Dictionary<string, object?>();

    /// <summary>
    /// 绑定一条命令。返回 false ⇒ <paramref name="issues"/> 非空（已含原因码），调用方不得继续使用 <paramref name="bound"/>。
    /// </summary>
    public static bool TryBind(
        MetaCommand? command,
        int index,
        out BoundInvocation? bound,
        out IReadOnlyList<InvocationIssue> issues)
    {
        bound = null;
        var list = new List<InvocationIssue>();

        var action = command?.Action?.Trim();
        if (string.IsNullOrWhiteSpace(action))
        {
            issues = list;
            return false;
        }

        var tokens = InvocationTokens.Tokenize(command!.Target);
        if (!InvocationSpecCatalog.TryResolve(action, command.Target, out var spec))
        {
            list.Add(new InvocationIssue(
                InvocationIssueKind.UnknownAction, index, action, tokens.FirstOrDefault(), null,
                NameSpaceId.None,
                $"命令 '{action}' 未在调用面声明（invocation.specs.json 是语法/目标空间/参数集的唯一来源）"));
            issues = list;
            return false;
        }

        if (tokens.Count < spec.MinTargetTokens)
        {
            list.Add(new InvocationIssue(
                InvocationIssueKind.TargetIncomplete, index, action, spec.SubAction,
                tokens.FirstOrDefault(), spec.TargetSpace,
                $"目标串 token 数不足：需 ≥{spec.MinTargetTokens}，实得 {tokens.Count}（'{command.Target}'）"));
            issues = list;
            return false;
        }

        var args = (IReadOnlyDictionary<string, object?>?)command.Parameters ?? NoArgs;

        // 参数白名单：声明键 ∪ probe 参数 ∪ 具名参数空间。
        foreach (var key in args.Keys)
        {
            if (spec.AcceptsArg(key)) continue;
            list.Add(new InvocationIssue(
                InvocationIssueKind.ArgumentNotDeclared, index, action, spec.SubAction, key,
                NameSpaceId.None,
                $"参数 '{key}' 不在命令 '{action}' 的声明面（声明: {spec.DescribeArgs()}）"));
        }

        // 数值参数取值校验（非法不静默降级）。
        if (args.ContainsKey(InvocationArgs.TimeoutKey))
        {
            var (_, error) = InvocationArgs.Timeout(args);
            if (error is not null)
                list.Add(new InvocationIssue(
                    InvocationIssueKind.ArgumentValueInvalid, index, action, spec.SubAction,
                    InvocationArgs.TimeoutKey, NameSpaceId.None, error));
        }

        // 名字归属：目标名 + 具名参数值。
        var claims = new List<NameClaim>();
        var targetName = NameOf(spec, tokens);
        if (spec.TargetSpace != NameSpaceId.None && !string.IsNullOrWhiteSpace(targetName))
            claims.Add(new NameClaim(spec.TargetSpace, targetName!));

        if (spec.ArgSpaces is { Count: > 0 })
        {
            foreach (var (argKey, argSpace) in spec.ArgSpaces)
            {
                var raw = InvocationArgs.Raw(args, argKey);
                if (raw is not null) claims.Add(new NameClaim(argSpace, raw));
            }
        }

        bound = new BoundInvocation(action, spec.SubAction, spec.TargetSpace, targetName, tokens, args, claims);
        issues = list;
        return true;
    }

    private static string? NameOf(InvocationSpec spec, IReadOnlyList<string> tokens)
    {
        if (spec.SubAction is not null)
            return tokens.Count > 1 ? tokens[1] : null;

        return tokens.Count > 0 ? tokens[0] : null;
    }
}
