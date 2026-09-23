using Plate.DslMethod.Parsing;

namespace Usharp.Syntax;

/// <summary>
/// 判据读法的一条规则（<c>rule &lt;层&gt; | label=&lt;标签&gt; | priority=&lt;n&gt; | when=&lt;轴.词元&gt;[,..]</c>）：
/// 全部必需词元命中即成立；面内 <c>priority</c> 越小越优先，命中即停。
/// <para>语义归既有 <c>DecisionGraph</c>；语言只声明边界（不新增裁决机制）。</para>
/// </summary>
/// <param name="LayerId">所属层号（须已声明，闸A <c>US1105</c>）。</param>
/// <param name="Label">产出标签（落在该层输出轴上；闸A 词表校验）。</param>
/// <param name="Priority">面内优先级（越小越优先；同层重复/缺失 ⇒ <c>US1103</c>）。</param>
/// <param name="When">必需词元引用（<c>&lt;轴&gt;.&lt;词元&gt;</c> 原文；空 = 非法）。</param>
/// <param name="CrossName">引用的复合特征名（<c>when=cross.&lt;名&gt;</c>；否则 null）。</param>
/// <param name="SourceFile">归因文件。</param>
/// <param name="LineNumber">归因行号。</param>
public sealed record UsharpDecisionRule(
    string LayerId,
    string Label,
    int Priority,
    IReadOnlyList<string> When,
    string? CrossName,
    string SourceFile,
    int LineNumber);

/// <summary>判据读法的一个层（<c>layer &lt;层&gt; | output=&lt;输出轴&gt; | absorb=&lt;标签&gt;[,..]</c>）。</summary>
/// <param name="LayerId">层号（图内唯一）。</param>
/// <param name="OutputAxis">该层专属输出轴（缺 ⇒ <c>US1105</c>）。</param>
/// <param name="Absorb">命中即吸收并短路下游的标签集（越出本层标签集 ⇒ <c>US1104</c>）。</param>
/// <param name="Rules">层内规则（声明序）。</param>
public sealed record UsharpDecisionLayer(
    string LayerId,
    string OutputAxis,
    IReadOnlyList<string> Absorb,
    IReadOnlyList<UsharpDecisionRule> Rules);

/// <summary>判据读法的一个复合特征（<c>cross &lt;名&gt; | when=&lt;轴.词元&gt;[,..]</c>；同名即共享，一处定义多面引用）。</summary>
/// <param name="Name">特征名。</param>
/// <param name="When">必需词元引用（空 ⇒ <c>US1106</c>）。</param>
public sealed record UsharpDecisionCross(string Name, IReadOnlyList<string> When);

/// <summary>
/// <c>[Matrix]</c> 判据读法（<c>over=&lt;kind=terms 空间&gt;</c>）的语法视图（E1 / U2b）。
/// <para>
/// 只做**语法**投影：层吸收合法性 / 优先级全序 / 词元可解析性等判据在闸A（<c>US1101</c>–<c>US1106</c>）；
/// 求值语义归既有 <c>BasicMethod.Declarative.Graph.DecisionGraph</c>（语言只声明边界）。
/// </para>
/// </summary>
/// <param name="MatrixId">矩阵号。</param>
/// <param name="Layers">层（声明序）。</param>
/// <param name="Crosses">复合特征（声明序）。</param>
/// <param name="UnparsedLines">无法归位的判据续行原文（空表 = 全部解析成功；禁止静默丢弃）。</param>
/// <param name="SourceFile">归因文件。</param>
/// <param name="LineNumber">归因行号。</param>
public sealed record UsharpDecisionDecl(
    string MatrixId,
    IReadOnlyList<UsharpDecisionLayer> Layers,
    IReadOnlyList<UsharpDecisionCross> Crosses,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>判据行的字段名集合（「疑似判据行但缺首词」的检出集；不静默丢弃）。</summary>
    private static readonly string[] DecisionFieldKeys =
    {
        UsharpLanguage.FieldOutput, UsharpLanguage.FieldAbsorb, UsharpLanguage.FieldPriority,
        UsharpLanguage.FieldWhen, UsharpLanguage.FieldLabel,
    };

    /// <summary>规则总数（取数：与声明面规则行数比对）。</summary>
    public int RuleCount => Layers.Sum(l => l.Rules.Count);

    /// <summary>是否有判据读法承载面（层或复合特征之一）。</summary>
    public bool HasDecisionSurface => Layers.Count > 0 || Crosses.Count > 0;

    /// <summary>按层号取层（未声明 ⇒ null）。</summary>
    /// <param name="layerId">层号。</param>
    public UsharpDecisionLayer? Layer(string layerId)
        => Layers.FirstOrDefault(l => string.Equals(l.LayerId, layerId, StringComparison.Ordinal));

    /// <summary>按名取复合特征（未声明 ⇒ null）。</summary>
    /// <param name="name">特征名。</param>
    public UsharpDecisionCross? Cross(string name)
        => Crosses.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 从矩阵声明投影判据读法（无 <c>layer</c>/<c>rule</c>/<c>cross</c> 行 ⇒ 空 <see cref="Layers"/>）。
    /// 声明序即语义：规则须在其所属层之后声明（乱序 ⇒ 记入 <see cref="UnparsedLines"/>，由闸A 报 <c>US1105</c>）。
    /// </summary>
    /// <param name="matrix">矩阵声明（其 <see cref="UsharpMatrixDecl.RawLines"/> 为续行真源）。</param>
    public static UsharpDecisionDecl From(UsharpMatrixDecl matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var layerDefs = new List<(string Line, string LayerId, string OutputAxis, IReadOnlyList<string> Absorb)>();
        var ruleDefs = new List<(string Line, UsharpDecisionRule Rule)>();
        var crosses = new List<UsharpDecisionCross>();
        var unparsed = new List<string>();

        // 第一段：逐行归类（层 / 规则 / 复合特征），形状非法者入 unparsed（不静默丢弃）。
        foreach (var line in matrix.RawLines)
        {
            if (!UsharpLanguage.TryReadRowMarker(line, out var marker))
            {
                if (LooksLikeDecisionField(line)) unparsed.Add(line);
                continue;
            }

            if (!TrySplitBody(line, marker, out var id, out var fields) || id.Length == 0)
            {
                unparsed.Add(line);
                continue;
            }

            if (string.Equals(marker, UsharpLanguage.RowLayerMarker, StringComparison.Ordinal))
            {
                var output = Field(fields, UsharpLanguage.FieldOutput);
                if (string.IsNullOrWhiteSpace(output)) { unparsed.Add(line); continue; }
                layerDefs.Add((line, id, output, FieldList(fields, UsharpLanguage.FieldAbsorb)));
                continue;
            }

            if (string.Equals(marker, UsharpLanguage.RowCrossMarker, StringComparison.Ordinal))
            {
                var when = FieldList(fields, UsharpLanguage.FieldWhen);
                if (when.Count == 0) { unparsed.Add(line); continue; }
                crosses.Add(new UsharpDecisionCross(id, when));
                continue;
            }

            // rule <层> | label=.. | priority=.. | when=..
            var label = Field(fields, UsharpLanguage.FieldLabel);
            var required = FieldList(fields, UsharpLanguage.FieldWhen);
            if (label.Length == 0 || required.Count == 0
                || !int.TryParse(Field(fields, UsharpLanguage.FieldPriority), out var priority))
            {
                unparsed.Add(line);
                continue;
            }

            var terms = new List<string>();
            string? crossName = null;
            var crossPrefix = UsharpLanguage.RowCrossMarker + UsharpLanguage.TermSeparator;
            foreach (var reference in required)
            {
                if (reference.StartsWith(crossPrefix, StringComparison.Ordinal))
                    crossName = reference[crossPrefix.Length..];
                else terms.Add(reference);
            }

            ruleDefs.Add((line, new UsharpDecisionRule(
                id, label, priority, terms, crossName, matrix.SourceFile, matrix.LineNumber)));
        }

        // 第二段：层按声明序建位（重复层号保留首位的规则归属，重复本身由闸A US1105 报），规则按声明序挂载。
        var attached = layerDefs.Select(_ => new List<UsharpDecisionRule>()).ToList();
        var ownerOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < layerDefs.Count; i++) ownerOf.TryAdd(layerDefs[i].LayerId, i);

        foreach (var (line, rule) in ruleDefs)
        {
            if (ownerOf.TryGetValue(rule.LayerId, out var owner)) attached[owner].Add(rule);
            else unparsed.Add(line);   // 层号不可解析 ⇒ 规则无处归位（不留悬空规则）
        }

        var layers = layerDefs.Select((d, i) =>
            new UsharpDecisionLayer(d.LayerId, d.OutputAxis, d.Absorb, attached[i])).ToList();

        return new UsharpDecisionDecl(matrix.MatrixId, layers, crosses, unparsed,
            matrix.SourceFile, matrix.LineNumber);
    }

    private static bool TrySplitBody(string line, string marker, out string id, out Dictionary<string, string> fields)
    {
        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        id = string.Empty;

        var segments = line[marker.Length..].Trim().Split('|');
        if (segments.Length == 0) return false;

        id = segments[0].Trim().TrimStart('=').Trim();
        for (var i = 1; i < segments.Length; i++)
        {
            // `k=v` 切分单源 = 解析器字段词法；散文段 ⇒ 整行不可解析（不静默丢弃）。
            var kv = FieldParser.TryParsePart(segments[i]);
            if (kv is null) return false;
            fields[kv.Value.Key] = kv.Value.Value;
        }
        return true;
    }

    private static string Field(Dictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static IReadOnlyList<string> FieldList(Dictionary<string, string> fields, string key)
        => Field(fields, key).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool LooksLikeDecisionField(string line)
    {
        foreach (var key in DecisionFieldKeys)
            if (line.Contains(key + "=", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
