using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;

namespace WorkFlowMethod.KeySpace.Adapters;

/// <summary>键集解析结果：成员 + 规划（含空集策略/理由）+ 可见诊断。</summary>
public sealed record KeySetParseResult(
    IReadOnlyList<string> Members,
    KeySetPlan Plan,
    KeyDiagnostic? Diagnostic)
{
    public bool IsEmpty => Members.Count == 0;
}

/// <summary>
/// A2：键集文本 → 成员 + <see cref="KeySetPlan"/> 的唯一解析入口。
/// <para>
/// 解析失败不再静默返回空集：产出 <see cref="KeyReasonCodes.Unparsable"/> 诊断，
/// 并以 <see cref="EmptySetPolicy.Report"/> 规划（空集必须带策略与理由）。
/// 非 JSON 的历史逗号形态作为兼容输入，保留 <see cref="KeyReasonCodes.LegacyForm"/> 诊断。
/// </para>
/// </summary>
public static class KeySetParse
{
    private static readonly KeySetPlanner Planner = new();

    public static KeySetParseResult Parse(string? raw, string family)
    {
        var members = new List<string>();
        KeyDiagnostic? diagnostic = null;
        var declaration = NodePortContractRegistry.Shared.Declaration;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            var text = raw.Trim();
            // 以 '[' / '{' 开头按 JSON 解析：非法 JSON 必须显式失败，不得冒充历史逗号形态。
            // 以 '"' 开头的单键字面量仍走历史逗号形态（兼容既有落库形态）。
            if (text.StartsWith("[", StringComparison.Ordinal)
                || text.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        diagnostic = KeyDiagnostic.Warn(
                            KeyReasonCodes.Unparsable, KeyResolution.Unparsable,
                            KeyRef.Of(family, text), "键集 JSON 根不是数组");
                    }
                    else
                    {
                        foreach (var item in document.RootElement.EnumerateArray())
                        {
                            var value = item.ValueKind == JsonValueKind.String
                                ? item.GetString()
                                : item.ToString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                members.Add(value);
                            }
                        }
                    }
                }
                catch (JsonException exception)
                {
                    diagnostic = KeyDiagnostic.Warn(
                        KeyReasonCodes.Unparsable, KeyResolution.Unparsable,
                        KeyRef.Of(family, text), "键集 JSON 解析失败: " + exception.Message);
                }
            }
            else
            {
                members.AddRange(SplitCommaForm(text));
                diagnostic = KeyDiagnostic.Info(
                    KeyReasonCodes.LegacyForm, KeyResolution.Resolved,
                    KeyRef.Of(family, text), "非 JSON 数组形态，按历史逗号形态解析");
            }
        }

        var plan = Planner.Plan(new KeySetPlanInput(
            RawMembers: members,
            Self: new KeyEntity(family, Array.Empty<KeyRef>()),
            MaxTargets: int.MaxValue,
            AllowFanout: true,
            EmptyPolicy: EmptySetPolicy.Report,
            Declaration: declaration));

        return new KeySetParseResult(
            plan.Targets.Select(target => target.Value).ToArray(),
            plan,
            diagnostic);
    }

    /// <summary>历史逗号形态（无 Regex / 无 <c>Split(',')</c>）：剥外部括号/引号，逐字符扫描。</summary>
    private static IEnumerable<string> SplitCommaForm(string text)
    {
        var start = text.StartsWith("[", StringComparison.Ordinal) ? 1 : 0;
        var end = text.EndsWith("]", StringComparison.Ordinal) ? text.Length - 1 : text.Length;
        var builder = new System.Text.StringBuilder();

        for (var index = start; index < end; index++)
        {
            var current = text[index];
            if (current == ',')
            {
                var token = builder.ToString().Trim().Trim('"');
                if (token.Length > 0)
                {
                    yield return token;
                }

                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        var last = builder.ToString().Trim().Trim('"');
        if (last.Length > 0)
        {
            yield return last;
        }
    }
}

/// <summary>诊断汇：把键空间诊断投影到统一日志（单一出口，禁止各调用点静默丢弃）。</summary>
public sealed class WorkflowKeyDiagnosticsSink : Modules.KeySpace.Abstractions.IKeyDiagnosticsSink
{
    public static readonly WorkflowKeyDiagnosticsSink Instance = new();

    public void Report(KeyDiagnostic diagnostic)
    {
        if (diagnostic is null) return;

        var payload = new
        {
            code = diagnostic.Code,
            severity = diagnostic.Severity.ToString(),
            resolution = diagnostic.Resolution.ToString(),
            key = diagnostic.Key?.Value,
            message = diagnostic.Message,
        };

        switch (diagnostic.Severity)
        {
            case KeyDiagnosticSeverity.Error:
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Error("keyspace.diagnostic", diagnostic.Message);
                break;
            case KeyDiagnosticSeverity.Warning:
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Warn(
                    "keyspace.diagnostic", diagnostic.Message, diagnostic.Key?.Value);
                break;
            default:
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log("keyspace.diagnostic", payload);
                break;
        }
    }
}
