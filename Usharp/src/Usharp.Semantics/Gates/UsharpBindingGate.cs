using Plate.DslMethod;
using Plate.DslMethod.Model;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 规则 ⑥：控制流绑定段词法问题（<c>in=</c>/<c>out=</c>/<c>routes=</c>）。
/// <para>
/// 词法内核 = <see cref="UsharpFieldBag"/>（经声明的 <c>BindingIssues</c> 透出）：
/// 重复键 → <c>US1019</c>；坏段（无 <c>=</c> / 空键 / 空值）→ <c>US0019</c> ——
/// 不再由本规则手写第二套 <c>IndexOf('=')</c> 解析（R-7 补丁守卫随之删除）。
/// </para>
/// </summary>
internal sealed class BindingDuplicateGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.binding.dup";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var doc in model.Documents)
        {
            foreach (var branch in UsharpSyntaxProjector.Switches(doc))
                Report(sink, $"[Switch] {branch.Name}", branch.BindingIssues, branch.SourceFile, branch.LineNumber);

            foreach (var call in UsharpSyntaxProjector.Calls(doc))
                Report(sink, $"[Call] {call.Name}", call.BindingIssues, call.SourceFile, call.LineNumber);

            foreach (var region in UsharpSyntaxProjector.Regions(doc))
                Report(sink, $"[Region] {region.Name}", region.BindingIssues, region.SourceFile, region.LineNumber);
        }
    }

    private static void Report(
        IConversionDiagnosticSink sink, string owner,
        IReadOnlyList<UsharpControlFlowBindingIssue> issues, string file, int line)
    {
        foreach (var issue in issues)
        {
            if (issue.IsDuplicate)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.BindingKeyDuplicate,
                    $"{owner}: {issue.Field}= 绑定键 '{issue.Segment}' 重复（同键多值静默覆盖；须唯一）",
                    file, line));
                continue;
            }

            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ControlFlowSegmentUnparsed,
                $"{owner}: {issue.Field}= 段 '{issue.Segment}' 无法解析（须为 <键>=<值>）",
                file, line));
        }
    }
}
