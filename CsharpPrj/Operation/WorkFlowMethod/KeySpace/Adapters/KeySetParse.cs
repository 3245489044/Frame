using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Parsing;
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
    /// <summary>
    /// 解析键集文本（实现单源 = <see cref="KeySetParser"/>，L2 通用件）；
    /// 声明来自当前组合根注入的节点端口契约宿主，仅做结果契约映射。
    /// </summary>
    public static KeySetParseResult Parse(string? raw, string family)
    {
        var parsed = KeySetParser.Parse(raw, family, NodePortContractRegistry.Shared.Declaration);
        return new KeySetParseResult(parsed.Members, parsed.Plan, parsed.Diagnostic);
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
