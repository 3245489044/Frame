using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A 诊断构造辅助：把 Usharp 的 <c>文件:行</c> 归因装配为内核 <see cref="ConversionDiagnostic"/>
/// （列号 Usharp 不产出 → 固定 0）。
/// </summary>
internal static class UsharpGateDiagnostics
{
    public static ConversionDiagnostic Error(string code, string message, string file, int line)
        => ConversionDiagnostic.Error(code, message, new SourceSpan(file, line, 0));

    public static ConversionDiagnostic Warn(string code, string message, string file, int line)
        => ConversionDiagnostic.Warning(code, message, new SourceSpan(file, line, 0));
}

/// <summary>规则 ①：<c>[Ref]</c> 坐标归属 + 闸A（空间须已声明、坐标须在空间内可解析）。</summary>
internal sealed class RefGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.ref";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var r in model.Refs)
        {
            if (string.IsNullOrWhiteSpace(r.SpaceId) || string.IsNullOrWhiteSpace(r.Key))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RefShapeInvalid,
                    $"[Ref] {r.Name}: 缺少 space/key（形如 space=Step key=cline_execute 或 Name: Step:cline_execute）",
                    r.SourceFile, r.LineNumber));
                continue;
            }
            if (!model.Spaces.TryGet(r.SpaceId, out _))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SpaceUnknown,
                    $"[Ref] {r.Name}: 引用了未声明的空间 '{r.SpaceId}'", r.SourceFile, r.LineNumber));
                continue;
            }
            if (!model.Spaces.HasCoordinate(r.SpaceId, r.Key, out _))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CoordUndeclared,
                    $"[Ref] {r.Name}: 坐标 '{r.Key}' 未在空间 '{r.SpaceId}' 声明（闸A）", r.SourceFile, r.LineNumber));

            // 作用域白名单 = [Obs] kind=scope 的既有声明面（零新语法，见设计方案 R9 方案 A）。
            if (r.Scope is { } scope && !(model.Obs?.HasScope(scope) ?? false))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.RefScopeUndeclared,
                    $"[Ref] {r.Name}: scope='{scope}' 未声明（闸A；白名单 = [Obs] <名>: kind=scope 声明面）",
                    r.SourceFile, r.LineNumber));
        }
    }
}

/// <summary>规则 ②：<c>[Matrix]</c> 空间存在 + 行输出/行内引用均须已声明。</summary>
internal sealed class MatrixGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.matrix";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var m in model.Matrices)
        {
            if (string.IsNullOrWhiteSpace(m.OverSpaceId) || !model.Spaces.TryGet(m.OverSpaceId, out var overSpace))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SpaceUnknown,
                    $"[Matrix] {m.MatrixId}: over 指向未声明的空间 '{m.OverSpaceId ?? "(缺 over=)"}'",
                    m.SourceFile, m.LineNumber));
                continue;
            }

            // 判据读法（over = kind=terms 空间）：续行是 layer/rule/cross，无「输出行」概念；
            // 其形态判据归 DecisionGateRule（US1101–US1106），此处放行（既有两读法行为零改）。
            if (overSpace.Kind == UsharpSpaceKind.Terms) continue;

            if (m.Rows.Count == 0 && !HasDecisionLines(m))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeclEmpty,
                    $"[Matrix] {m.MatrixId}: 未声明任何输出行（缩进块为空）", m.SourceFile, m.LineNumber));

            var readScope = model.Spaces.ReadScope(m.OverSpaceId);
            foreach (var row in m.Rows)
            {
                // 写侧（设计方案 §3.2）：输出坐标须**严格**属自身层 ⇒ 属祖先层即「越界写」（US1010，比 US1003 更精确的归因）
                if (!model.Spaces.HasCoordinate(m.OverSpaceId, row.OutputKey, out _))
                {
                    var ancestor = model.Spaces.AncestorSpaces(m.OverSpaceId)
                        .FirstOrDefault(a => model.Spaces.HasCoordinate(a, row.OutputKey, out _));

                    if (ancestor is not null)
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MatrixRowWriteOutOfSubSpace,
                            $"[Matrix] {m.MatrixId}: 输出坐标 '{row.OutputKey}' 属祖先空间 '{ancestor}'"
                            + $" 但不属自身空间 '{m.OverSpaceId}'（越界写 / 父子双写）",
                            m.SourceFile, m.LineNumber));
                    else
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MatrixRowOutUndeclared,
                            $"[Matrix] {m.MatrixId}: 输出坐标 '{row.OutputKey}' 未在空间 '{m.OverSpaceId}' 声明（闸A）",
                            m.SourceFile, m.LineNumber));
                }

                // 读侧（设计方案 §3.2 + §10.4 实施修正 ④）：可读域 = 自身层 ∪ 祖先闭包（兄弟块依赖走显式 connects=）
                foreach (var input in row.InputRefs)
                    if (!model.Spaces.IsReadable(m.OverSpaceId, input, out _))
                    {
                        // 归因分工：由非祖先块写入的坐标归 US1017（跨块写依赖未声明，ConnectGateRule 报，更精确）；
                        // 其余（本层体系内无处可解析）才归 US1004。
                        if (model.NonAncestorWriters(m.OverSpaceId, input).Count > 0) continue;

                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.MatrixRowInputUndeclared,
                            $"[Matrix] {m.MatrixId}: 行 '{row.OutputKey}' 引用了未声明坐标 '{input}'"
                            + $"（闸A；可读域 = over 空间 '{m.OverSpaceId}'"
                            + (readScope.Length == 0 ? "，无祖先层" : $" ∪ 祖先闭包 {readScope}")
                            + "）",
                            m.SourceFile, m.LineNumber));
                    }
            }
        }
    }
    /// <summary>
    /// 是否含判据读法续行（<c>layer</c>/<c>rule</c>/<c>cross</c>）——读法错误归 <c>DecisionGateRule</c>（<c>US1105</c>），
    /// 此处不再叠加 <c>US0003</c>（同一缺陷只报一次）。
    /// </summary>
    private static bool HasDecisionLines(UsharpMatrixDecl matrix)
    {
        foreach (var line in matrix.RawLines)
            if (UsharpLanguage.TryReadRowMarker(line, out _)) return true;
        return false;
    }
}

/// <summary>规则 ③：<c>[Flow]</c> 空间存在 + 步键须已声明（★ 原 C1 判据）+ 子链须已声明。</summary>
internal sealed class FlowGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.flow";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var f in model.Flows)
        {
            if (string.IsNullOrWhiteSpace(f.OverSpaceId) || !model.Spaces.TryGet(f.OverSpaceId, out _))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SpaceUnknown,
                    $"[Flow] {f.FlowId}: over 指向未声明的空间 '{f.OverSpaceId ?? "(缺 over=)"}'",
                    f.SourceFile, f.LineNumber));
                continue;
            }
            if (f.Steps.Count == 0)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DeclEmpty,
                    $"[Flow] {f.FlowId}: 未声明任何步骤（缩进块为空）", f.SourceFile, f.LineNumber));

            foreach (var step in f.Steps)
            {
                if (!model.Spaces.HasCoordinate(f.OverSpaceId, step.Key, out _))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.FlowStepUndeclared,
                        $"[Flow] {f.FlowId}: 步骤键 '{step.Key}' 未在空间 '{f.OverSpaceId}' 声明（闸A；现状等价缺陷=声明被静默丢弃）",
                        f.SourceFile, f.LineNumber));
                else if (!model.NodeNames.Contains(step.Key))
                    sink.Report(UsharpGateDiagnostics.Warn(UsharpCodes.EdgePeerUnknown,
                        $"[Flow] {f.FlowId}: 步骤键 '{step.Key}' 尚无对应 [Node] 声明（可执行体未登记）——与发射层 US5002 同条件",
                        f.SourceFile, f.LineNumber));

                if (step.SubFlow is not null && !model.FlowIds.Contains(step.SubFlow))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.SubFlowUnknown,
                        $"[Flow] {f.FlowId}: 子链 '{step.SubFlow}' 未声明（工作流连接链的引用必须可解析）",
                        f.SourceFile, f.LineNumber));
            }
        }
    }
}

/// <summary>规则 ④：<c>[CR]</c> 悬空（复用 DslIndex；跨库引用需同批装载才可解析 → Warning）。</summary>
internal sealed class CrossReferenceGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.crossref";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var doc in model.Documents)
            foreach (var decl in doc.Declarations)
                foreach (var r in decl.References)
                    if (!model.Index.IsDefined(r))
                        sink.Report(UsharpGateDiagnostics.Warn(UsharpCodes.CrDangling,
                            $"{decl.Prefix} {decl.Name}: [CR] 悬空（符号索引不可解析）{r.Raw}",
                            decl.SourceFile, decl.LineNumber));
    }
}

/// <summary>
/// 规则 ⑤：<c>[Obs]</c> 闸A（第五语言单元）：实体/作用域可解析 + 步骤发射绑定可解析 +
/// 枚举字段自洽 + 声明接线可见化。把运行期「未注册事件即抛」前移到编译期。
/// <c>US6001</c> 实体/作用域指向未声明；<c>US6002</c> 步骤 <c>emit=</c> 引用未声明事件；
/// <c>US6004</c> 枚举字段未出现在必填字段；<c>US6005</c> 事件声明无任何步骤消费（告警，显式可见化）。
/// </summary>
internal sealed class ObsGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.obs";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var obs = model.Obs;
        if (obs is null) return;

        foreach (var decl in obs.Declarations)
        {
            foreach (var (field, value) in new[] { (UsharpLanguage.FieldScope, decl.Scope), (UsharpLanguage.FieldParent, decl.Parent) })
            {
                if (value is null || obs.HasScope(value)) continue;
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ObsEntityScopeUndefined,
                    $"[Obs] {decl.Name}: {field}='{value}' 指向未声明的作用域（闸A；先声明 [Obs] …: kind=scope）",
                    decl.SourceFile, decl.LineNumber));
            }

            if (decl.Kind != UsharpObsKind.Event) continue;

            if (decl.Entity is null)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ObsEntityScopeUndefined,
                    $"[Obs] {decl.Name}: 事件缺 entity=（须指向已声明实体）", decl.SourceFile, decl.LineNumber));
            else if (!obs.HasEntity(decl.Entity))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ObsEntityScopeUndefined,
                    $"[Obs] {decl.Name}: entity='{decl.Entity}' 指向未声明的实体（闸A；先声明 [Obs] …: kind=entity）",
                    decl.SourceFile, decl.LineNumber));

            foreach (var field in decl.Enums.Keys)
                if (!decl.RequiresField(field))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ObsEnumFieldNotRequired,
                        $"[Obs] {decl.Name}: enum 字段 '{field}' 未出现在 required=（或 fields=）中（声明自洽性）",
                        decl.SourceFile, decl.LineNumber));
        }

        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in model.Flows)
            foreach (var step in flow.Steps)
                foreach (var kind in step.Emits)
                {
                    consumed.Add(kind);
                    if (!obs.HasEvent(kind))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.ObsEmitUndeclared,
                            $"[Flow] {flow.FlowId}: 步骤 '{step.Key}' emit='{kind}' 未声明事件（闸A；"
                            + "运行期未注册事件异常前移到编译期）",
                            flow.SourceFile, flow.LineNumber));
                }

        foreach (var decl in obs.Events)
            if (!consumed.Contains(decl.Name))
                sink.Report(UsharpGateDiagnostics.Warn(UsharpCodes.ObsEventUnconsumed,
                    $"[Obs] {decl.Name}: 事件声明尚无 [Flow] 步骤 emit= 消费（声明未接线，显式可见化）",
                    decl.SourceFile, decl.LineNumber));
    }
}

/// <summary>
/// 规则 ⑫：<c>[Domain]</c> 域定义面闸A（U1）：身份占位符 ⊇ 位置键模板占位符 / 位置 id 唯一 /
/// <c>selection=</c> 与 <c>retention=</c> 引用在册 / 形态自洽（空 placements / 不可解析续行 / 缺必填字段）。
/// </summary>
internal sealed class DomainGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.domain";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var domain in model.Domains)
        {
            foreach (var line in domain.UnparsedLines)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DomainShapeInvalid,
                    $"[Domain] {domain.DomainId}: 续行不可解析 '{line}'（须为 identity… / context… / sequence… 或子块内 k=v 项）",
                    domain.SourceFile, domain.LineNumber));
            }

            if (string.IsNullOrWhiteSpace(domain.Identity))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DomainIdentityInvalid,
                    $"[Domain] {domain.DomainId}: 缺 identity=（身份模板为位置键反解的唯一依据）",
                    domain.SourceFile, domain.LineNumber));
            }

            if (domain.Placements.Count == 0)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DomainShapeInvalid,
                    $"[Domain] {domain.DomainId}: placements 为空（域定义面须至少声明一个位置）",
                    domain.SourceFile, domain.LineNumber));
            }

            var identityPlaceholders = Placeholders(domain.Identity);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var placement in domain.Placements)
            {
                if (!seenIds.Add(placement.Id))
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DomainPlacementIdDuplicate,
                        $"[Domain] {domain.DomainId}: 位置 id '{placement.Id}' 重复（位置 id 须唯一）",
                        domain.SourceFile, domain.LineNumber));
                }

                foreach (var placeholder in Placeholders(placement.Key))
                {
                    if (!identityPlaceholders.Contains(placeholder))
                    {
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DomainIdentityInvalid,
                            $"[Domain] {domain.DomainId}: 位置 '{placement.Id}' 键模板占位符 '{{{placeholder}}}' "
                            + "不被身份占位符覆盖（身份占位符 ⊇ 位置键模板占位符）",
                            domain.SourceFile, domain.LineNumber));
                    }
                }

                if (!domain.Selections.ContainsKey(placement.Selection))
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DomainSelectionUndeclared,
                        $"[Domain] {domain.DomainId}: 位置 '{placement.Id}' 的 selection='{placement.Selection}' 未在 selections 词表声明",
                        domain.SourceFile, domain.LineNumber));
                }

                if (!domain.Retentions.ContainsKey(placement.Retention))
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.DomainRetentionUndeclared,
                        $"[Domain] {domain.DomainId}: 位置 '{placement.Id}' 的 retention='{placement.Retention}' 未在 retentions 词表声明",
                        domain.SourceFile, domain.LineNumber));
                }
            }
        }
    }

    /// <summary>键模板占位符集（<c>{name}</c> → name；无占位符 ⇒ 空集）。</summary>
    private static HashSet<string> Placeholders(string template)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(template)) return set;

        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0) break;
            var close = template.IndexOf('}', open + 1);
            if (close < 0) break;
            var name = template[(open + 1)..close].Trim();
            if (name.Length > 0) set.Add(name);
            index = close + 1;
        }
        return set;
    }
}

/// <summary>
/// 规则 ⑬：<c>[Gather]</c> 聚拢声明形态闸A（US7101–US7106）。
/// <para>
/// 判据：<c>kind-field</c> 非空 / 族名唯一 / 塑形行含 <c>family=</c> / <c>summary</c> 声明则非空 /
/// 通配塑形 <c>*</c> 存在 / 续行可解析。全部为可判定结构事实（不引执行者语义）。
/// </para>
/// </summary>
internal sealed class GatherGateRule : IGateRule<UsharpGateModel>
{
    /// <summary>通配塑形键（未显式声明 kind 的默认塑形；单源 = <c>NodeGatherSpec.DefaultShapingKey</c> 同值）。</summary>
    private const string WildcardKind = "*";

    public string Name => "usharp.gather";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        foreach (var gather in model.Gathers)
        {
            foreach (var line in gather.UnparsedLines)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.GatherShapeInvalid,
                    $"[Gather] {gather.DomainId}: 续行不可解析 '{line}'"
                    + "（须为 kind-field… / group-by… / family… / shaping…）",
                    gather.SourceFile, gather.LineNumber));
            }

            if (string.IsNullOrWhiteSpace(gather.KindField))
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.GatherKindFieldMissing,
                    $"[Gather] {gather.DomainId}: 缺 kind-field=（判别字段为族/塑形定位的唯一依据）",
                    gather.SourceFile, gather.LineNumber));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var duplicate in gather.DuplicateFamilies)
            {
                if (!seen.Add(duplicate)) continue;
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.GatherFamilyDuplicate,
                    $"[Gather] {gather.DomainId}: 族名 '{duplicate}' 重复（族声明序为聚拢组间序的唯一依据）",
                    gather.SourceFile, gather.LineNumber));
            }

            var hasWildcard = false;
            foreach (var shaping in gather.Shapings)
            {
                if (string.Equals(shaping.Kind, WildcardKind, StringComparison.Ordinal)) hasWildcard = true;

                if (string.IsNullOrWhiteSpace(shaping.Family))
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.GatherShapingFamilyMissing,
                        $"[Gather] {gather.DomainId}: 塑形 '{shaping.Kind}' 缺 family=（族归属）",
                        gather.SourceFile, gather.LineNumber));
                }

                var summaryRaw = shaping.Field(UsharpLanguage.FieldSummary);
                if (summaryRaw is not null && shaping.SummaryList.Count == 0)
                {
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.GatherShapingSummaryEmpty,
                        $"[Gather] {gather.DomainId}: 塑形 '{shaping.Kind}' 的 summary 为空（须为逗号列表且非空）",
                        gather.SourceFile, gather.LineNumber));
                }
            }

            if (gather.Shapings.Count == 0 || !hasWildcard)
            {
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.GatherWildcardShapingMissing,
                    $"[Gather] {gather.DomainId}: 缺通配塑形 '{WildcardKind}'（治「未声明 kind 被丢行」）",
                    gather.SourceFile, gather.LineNumber));
            }
        }
    }
}

