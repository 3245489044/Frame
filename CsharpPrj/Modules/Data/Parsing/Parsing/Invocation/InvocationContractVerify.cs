using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Parsing.Models;
using Modules.Parsing.Invocation;

namespace Modules.Parsing.Invocation;

/// <summary>
/// 调用面契约自证（正/负对照）：声明面、绑定、参数、命名空间、契约门各一组。
/// 镜像 <c>ContractFramework.Verify.ContractFrameworkVerify</c>：只返回结论与逐项摘要，不抛。
/// </summary>
public static class InvocationContractVerify
{
    public static bool Run(out string summary)
    {
        var results = new List<string>();
        var passed = true;

        passed &= Case("声明面·加载零问题", SpecsWellFormed, results);
        passed &= Case("声明面·已声明动作数 ≥ 20", SpecDeclared, results);
        passed &= Case("分词·引号成组", TokenizeQuoted, results);
        passed &= Case("绑定·未声明动作 ⇒ R1", BindingUnknownAction, results);
        passed &= Case("绑定·目标 token 不足 ⇒ R1", BindingIncompleteTarget, results);
        passed &= Case("绑定·参数未声明 ⇒ R2", BindingUnknownArgument, results);
        passed &= Case("绑定·非法 timeout ⇒ R2", BindingInvalidTimeout, results);
        passed &= Case("命名空间·清单命中/未命中/无来源三态", NameSpaceTriState, results);
        passed &= Case("契约门·干净序列 ⇒ 放行（不阻断）", GateClean, results);
        passed &= Case("契约门·违约序列 ⇒ 报告非空", GateViolation, results);
        passed &= Case("参数读取·声明键可读（值内嵌无第二通道）", ParamReadable, results);
        passed &= Case("参数读取·空白视为缺失", ParamBlankIsMissing, results);
        passed &= Case("判据·回显恒真被守卫拦截", EchoGuardBlocks, results);
        passed &= Case("声明面·未列子动作回退动作级声明", SubActionFallback, results);

        summary = $"InvocationContractVerify: {(passed ? "PASS" : "FAIL")}{Environment.NewLine}"
                  + string.Join(Environment.NewLine, results);
        return passed;
    }

    private static bool Case(string name, Func<bool> body, List<string> results)
    {
        try
        {
            var ok = body();
            results.Add($"[{(ok ? "PASS" : "FAIL")}] {name}");
            return ok;
        }
        catch (Exception ex)
        {
            results.Add($"[FAIL] {name}: {ex.Message}");
            return false;
        }
    }

    private static bool SpecsWellFormed() => InvocationSpecCatalog.IsWellFormed;

    private static bool SpecDeclared() => InvocationSpecCatalog.All.Count >= 20;

    private static bool TokenizeQuoted()
    {
        var tokens = InvocationTokens.Tokenize("container \"Main View\" count>0");
        return tokens.Count == 3 && tokens[1] == "Main View" && tokens[2] == "count>0";
    }

    private static bool BindingUnknownAction()
        => !InvocationBinding.TryBind(Command("ghost-action", "x"), 0, out _, out var issues)
           && issues.Any(i => i.ReasonCode == InvocationReasonCodes.ActionNotDeclared);

    private static bool BindingIncompleteTarget()
        => !InvocationBinding.TryBind(Command("assert", "container"), 0, out _, out var issues)
           && issues.Any(i => i.ReasonCode == InvocationReasonCodes.TargetIncomplete);

    private static bool BindingUnknownArgument()
    {
        var command = Command("validate", "someControl");
        command.Parameters = new Dictionary<string, object> { ["nope"] = "1" };
        return InvocationBinding.TryBind(command, 0, out _, out var issues)
               && issues.Any(i => i.ReasonCode == InvocationReasonCodes.ArgumentNotDeclared);
    }

    private static bool BindingInvalidTimeout()
    {
        var command = Command("wait-until", "probeFn");
        command.Parameters = new Dictionary<string, object>
        {
            ["expectedContains"] = "x",
            ["timeout"] = "abc",
        };
        return InvocationBinding.TryBind(command, 0, out _, out var issues)
               && issues.Any(i => i.ReasonCode == InvocationReasonCodes.ArgumentValueInvalid);
    }

    private static bool NameSpaceTriState()
    {
        var index = new NameSpaceIndex();
        index.Register(new SetNameSpaceSource(NameSpaceId.Control, ["known"], NameSpaceOrigin.Declared));

        var found = index.Resolve(NameSpaceId.Control, "known");
        var missing = index.Resolve(NameSpaceId.Control, "other");
        var unavailable = index.Resolve(NameSpaceId.Form, "any");

        return found.Found
               && found.Origin == NameSpaceOrigin.Declared
               && missing.Kind == NameHitKind.NotFound
               && unavailable.IsUnavailable;
    }

    private static bool GateClean()
    {
        // 只声明面校验（不依赖运行期探针）：用 list 命令（TargetSpace=None）。
        var report = InvocationContractGate.Evaluate([Command("list", null)], nameof(GateClean));
        return report.IsClean && !report.Blocking;
    }

    private static bool GateViolation()
    {
        var report = InvocationContractGate.Evaluate([Command("ghost-action", "x")], nameof(GateViolation));
        return !report.IsClean && report.Issues.Any(i => i.ReasonCode == InvocationReasonCodes.ActionNotDeclared);
    }

    /// <summary>可读性自证：每个声明键经唯一读取器必须可读（曾出现「声明存在但读取即崩」的空引用缺陷）。</summary>
    private static bool ParamReadable()
    {
        foreach (var spec in InvocationSpecCatalog.All)
        {
            if (spec.Args is null) continue;
            foreach (var key in spec.Args)
            {
                var args = new Dictionary<string, object?> { [key] = "x1" };
                var read = InvocationArgs.Read(args, key);
                if (!read.IsUsable || !string.Equals(read.Raw, "x1", StringComparison.Ordinal)) return false;
                if (!string.Equals(InvocationArgs.Text(args, key), "x1", StringComparison.Ordinal)) return false;
            }
        }

        return true;
    }

    private static bool ParamBlankIsMissing()
    {
        var args = new Dictionary<string, object?> { ["k"] = "   " };
        return !InvocationArgs.Read(args, "k").Present && InvocationArgs.Text(args, "k") is null;
    }

    private static bool EchoGuardBlocks()
    {
        var args = new Dictionary<string, object?> { ["taskKey"] = "tp_x" };
        return ProbeEchoGuard.IsTautological("tp_x", "AgentUi_TaskPoolGetStatus", args)
               && !ProbeEchoGuard.IsTautological("CLINE_READ_FILE_OK", "AgentUi_RunJson", args);
    }

    private static bool SubActionFallback()
        => InvocationSpecCatalog.TryResolve("assert", "skin-binding skin", out var spec)
           && spec.Action == "assert";

    private static MetaCommand Command(string action, string? target)
        => new()
        {
            Verb = MetaVerb.Do,
            Action = action,
            Target = target,
            OriginalCommand = $"{action} {target}".Trim(),
        };
}
