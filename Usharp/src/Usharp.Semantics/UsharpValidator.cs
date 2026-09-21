using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 装载期闸A（规则 R3）：任何坐标引用必须已在某已声明 <c>[Space]</c> 内可解析，否则**编译报错**
/// ——这是「未声明即静默失效」类缺陷（原 C1：_NextNodeConfig 声明被静默截断）的语言级判据。
/// 与 <c>ParameterMatrix.Row</c> 的装载期闸A 同语义，覆盖面从「参数轴」扩到「空间键 + 参数轴 + 词元」。
/// </summary>
public static class UsharpValidator
{
    public static IReadOnlyList<UsharpDiagnostic> Validate(
        IReadOnlyList<DslDocument> documents,
        DslIndex index,
        SpaceRegistry spaces,
        IReadOnlyList<UsharpMatrixDecl> matrices,
        IReadOnlyList<UsharpFlowDecl> flows,
        IReadOnlyList<UsharpRefDecl> refs)
    {
        var issues = new List<UsharpDiagnostic>();
        var flowIds = new HashSet<string>(flows.Select(f => f.FlowId), StringComparer.Ordinal);
        var nodeNames = new HashSet<string>(
            documents.SelectMany(d => d.Declarations)
                     .Where(d => d.Kind == DslSymbolKind.Node)
                     .Select(d => d.Name), StringComparer.Ordinal);

        // ① [Ref]：坐标归属 + 闸A
        foreach (var r in refs)
        {
            if (string.IsNullOrWhiteSpace(r.SpaceId) || string.IsNullOrWhiteSpace(r.Key))
            {
                issues.Add(UsharpDiagnostic.Error(UsharpCodes.RefShapeInvalid,
                    $"[Ref] {r.Name}: 缺少 space/key（形如 space=Step key=cline_execute 或 Name: Step:cline_execute）",
                    r.SourceFile, r.LineNumber));
                continue;
            }
            if (!spaces.TryGet(r.SpaceId, out _))
            {
                issues.Add(UsharpDiagnostic.Error(UsharpCodes.SpaceUnknown,
                    $"[Ref] {r.Name}: 引用了未声明的空间 '{r.SpaceId}'", r.SourceFile, r.LineNumber));
                continue;
            }
            if (!spaces.HasCoordinate(r.SpaceId, r.Key, out _))
                issues.Add(UsharpDiagnostic.Error(UsharpCodes.CoordUndeclared,
                    $"[Ref] {r.Name}: 坐标 '{r.Key}' 未在空间 '{r.SpaceId}' 声明（闸A）", r.SourceFile, r.LineNumber));
        }

        // ② [Matrix]：空间存在 + 行输出/行内引用均须已声明
        foreach (var m in matrices)
        {
            if (string.IsNullOrWhiteSpace(m.OverSpaceId) || !spaces.TryGet(m.OverSpaceId, out _))
            {
                issues.Add(UsharpDiagnostic.Error(UsharpCodes.SpaceUnknown,
                    $"[Matrix] {m.MatrixId}: over 指向未声明的空间 '{m.OverSpaceId ?? "(缺 over=)"}'",
                    m.SourceFile, m.LineNumber));
                continue;
            }
            if (m.Rows.Count == 0)
                issues.Add(UsharpDiagnostic.Error(UsharpCodes.DeclEmpty,
                    $"[Matrix] {m.MatrixId}: 未声明任何输出行（缩进块为空）", m.SourceFile, m.LineNumber));

            foreach (var row in m.Rows)
            {
                if (!spaces.HasCoordinate(m.OverSpaceId, row.OutputKey, out _))
                    issues.Add(UsharpDiagnostic.Error(UsharpCodes.MatrixRowOutUndeclared,
                        $"[Matrix] {m.MatrixId}: 输出坐标 '{row.OutputKey}' 未在空间 '{m.OverSpaceId}' 声明（闸A）",
                        m.SourceFile, m.LineNumber));
                foreach (var input in row.InputRefs)
                    if (!spaces.HasCoordinate(m.OverSpaceId, input, out _))
                        issues.Add(UsharpDiagnostic.Error(UsharpCodes.MatrixRowInputUndeclared,
                            $"[Matrix] {m.MatrixId}: 行 '{row.OutputKey}' 引用了未声明坐标 '{input}'（闸A）",
                            m.SourceFile, m.LineNumber));
            }
        }

        // ③ [Flow]：空间存在 + 步键须已声明（★ 原 C1 判据）+ 子链须已声明
        foreach (var f in flows)
        {
            if (string.IsNullOrWhiteSpace(f.OverSpaceId) || !spaces.TryGet(f.OverSpaceId, out _))
            {
                issues.Add(UsharpDiagnostic.Error(UsharpCodes.SpaceUnknown,
                    $"[Flow] {f.FlowId}: over 指向未声明的空间 '{f.OverSpaceId ?? "(缺 over=)"}'",
                    f.SourceFile, f.LineNumber));
                continue;
            }
            if (f.Steps.Count == 0)
                issues.Add(UsharpDiagnostic.Error(UsharpCodes.DeclEmpty,
                    $"[Flow] {f.FlowId}: 未声明任何步骤（缩进块为空）", f.SourceFile, f.LineNumber));

            foreach (var step in f.Steps)
            {
                if (!spaces.HasCoordinate(f.OverSpaceId, step.Key, out _))
                    issues.Add(UsharpDiagnostic.Error(UsharpCodes.FlowStepUndeclared,
                        $"[Flow] {f.FlowId}: 步骤键 '{step.Key}' 未在空间 '{f.OverSpaceId}' 声明（闸A；现状等价缺陷=声明被静默丢弃）",
                        f.SourceFile, f.LineNumber));
                else if (nodeNames.Count > 0 && !nodeNames.Contains(step.Key))
                    issues.Add(UsharpDiagnostic.Warn(UsharpCodes.EdgePeerUnknown,
                        $"[Flow] {f.FlowId}: 步骤键 '{step.Key}' 尚无对应 [Node] 声明（可执行体未登记）",
                        f.SourceFile, f.LineNumber));

                if (step.SubFlow is not null && !flowIds.Contains(step.SubFlow))
                    issues.Add(UsharpDiagnostic.Error(UsharpCodes.SubFlowUnknown,
                        $"[Flow] {f.FlowId}: 子链 '{step.SubFlow}' 未声明（工作流连接链的引用必须可解析）",
                        f.SourceFile, f.LineNumber));
            }
        }

        // ④ [CR] 悬空（复用 DslIndex；跨库引用需同批装载才可解析 → Warning）
        foreach (var doc in documents)
            foreach (var decl in doc.Declarations)
                foreach (var r in decl.References)
                    if (!index.IsDefined(r))
                        issues.Add(UsharpDiagnostic.Warn(UsharpCodes.CrDangling,
                            $"{decl.Prefix} {decl.Name}: [CR] 悬空（符号索引不可解析）{r.Raw}",
                            decl.SourceFile, decl.LineNumber));

        return issues;
    }
}
