using System.Text.Json;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;

namespace Modules.KeySpace.Parsing;

/// <summary>键集解析结果：成员 + 规划（含空集策略/理由）+ 可见诊断。</summary>
/// <param name="Members">归一键集成员。</param>
/// <param name="Plan">键集规划（空集策略/理由随行）。</param>
/// <param name="Diagnostic">解析诊断（可空 = 无异常）。</param>
public sealed record KeySetParseResult(
    IReadOnlyList<string> Members,
    KeySetPlan Plan,
    KeyDiagnostic? Diagnostic)
{
    /// <summary>是否空集。</summary>
    public bool IsEmpty => Members.Count == 0;
}

/// <summary>
/// 键集文本 → 成员 + <see cref="KeySetPlan"/> 的通用解析入口（声明由调用方注入，不读进程静态）。
/// <para>
/// 口径：JSON 数组为规范形态；<c>[</c>/<c>{</c> 开头但非合法 JSON ⇒ 显式 <see cref="KeyReasonCodes.Unparsable"/> 诊断；
/// 其余按历史逗号形态兼容并产 <see cref="KeyReasonCodes.LegacyForm"/> 诊断；空集必带 <see cref="EmptySetPolicy.Report"/> 理由。
/// </para>
/// </summary>
public static class KeySetParser
{
    private static readonly KeySetPlanner Planner = new();

    /// <summary>解析键集文本。</summary>
    /// <param name="raw">原始文本（可空）。</param>
    /// <param name="family">键族名（诊断用）。</param>
    /// <param name="declaration">键空间声明（成员归一依据）。</param>
    /// <param name="emptyPolicy">空集策略（缺省 Report：空集必须可见）。</param>
    public static KeySetParseResult Parse(
        string? raw,
        string family,
        IKeySpaceDeclaration declaration,
        EmptySetPolicy emptyPolicy = EmptySetPolicy.Report)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var members = new List<string>();
        KeyDiagnostic? diagnostic = null;

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
            EmptyPolicy: emptyPolicy,
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
