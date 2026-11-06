using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// 一条位置声明（<c>[Domain]</c> 的 <c>placements</c> 块内一行）——位置 id + 层 + 载体 + 键模板 +
/// 形态 + 保留等级 + 选择名 + 写入者（+ 可选 <c>state=</c> 存量态）。
/// </summary>
/// <param name="Id">位置 id（同域内唯一；闸A 判据）。</param>
/// <param name="Level">存储层级（archive / session / task / node …）。</param>
/// <param name="Carrier">载体 id（喂 <c>ICarrierRegistry</c>）。</param>
/// <param name="Key">位置键模板（占位符须 ⊆ 身份占位符 ∪ 该位置 local 坐标；闸A 判据）。</param>
/// <param name="Form">形态（raw / view / stream）。</param>
/// <param name="Retention">保留等级名（须在 <c>retentions</c> 词表在册；闸A 判据）。</param>
/// <param name="Selection">选择名（须在 <c>selections</c> 词表在册；闸A 判据）。</param>
/// <param name="Writer">写入者 id（同域内 (domain,placement)→writer 一对一）。</param>
/// <param name="State">存量态（<c>legacy</c> = 兼容读禁写；缺省 null）。</param>
public sealed record UsharpPlacementDecl(
    string Id,
    string Level,
    string Carrier,
    string Key,
    string Form,
    string Retention,
    string Selection,
    string Writer,
    string? State,
    string? RecordType = null,
    string? ThreadCoordinate = null,
    string? PayloadSchema = null,
    string? Projection = null);

/// <summary>身份坐标绑定：<c>container | family=agent.task | axis=container | canonical=required</c>。</summary>
/// <param name="Name">身份占位符名（须与 identity 模板一致）。</param>
/// <param name="Family">键族 id（须在 [KeySpace] 已声明）。</param>
/// <param name="Axis">族内轴名（须在 [KeySpace] 已声明）。</param>
/// <param name="Canonical">规范值姿态（required = 必须经声明规则解析）。</param>
public sealed record UsharpCoordinateDecl(string Name, string Family, string Axis, string Canonical);

/// <summary>
/// <c>[Domain] Name</c> 的语法视图（节点域定义面的事实唯一真源；U1）。
/// <para>
/// 书写形态（**扁平续行**，复用既有续行通道，零第二表层风格）：头 = <c>[Domain] &lt;domainId&gt;</c>；
/// 续行 = <c>identity …</c> / <c>context a, b</c> / <c>sequence a, b</c> + 三个子块首词
/// <c>placements</c> / <c>selections</c> / <c>retentions</c>（子块内逐行一项，不依赖缩进层级——续行经 Trim 后无缩进信息）。
/// </para>
/// <para>
/// 口径：<c>selections</c> 只做「名 → 谓词 token」登记（谓词求值仍归 C# <c>SelectionSet</c>，与 X1 一致）；
/// <c>position</c> 与 <c>placement.ushar</c> 的分工：域声明只给位置 id，loc→carrier 反解仍归
/// <c>[Matrix] LocToPlacement</c>（不重复）。
/// </para>
/// </summary>
public sealed record UsharpDomainDecl(
    string DomainId,
    string Identity,
    IReadOnlyList<string> Context,
    IReadOnlyList<string> Sequence,
    IReadOnlyList<UsharpPlacementDecl> Placements,
    IReadOnlyDictionary<string, string> Selections,
    IReadOnlyDictionary<string, string> Retentions,
    IReadOnlyDictionary<string, string> Marks,
    IReadOnlyList<string> Origins,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyDictionary<string, string> Behaviors,
    IReadOnlyList<UsharpCoordinateDecl> Coordinates,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    private enum Section
    {
        None,
        Placements,
        Selections,
        Retentions,
        Marks,
        Origins,
        Tags,
        Behaviors,
        Coordinates,
    }

    /// <summary>投影单条 <c>[Domain]</c> 声明（无名 ⇒ null；无法解析的续行落入 <see cref="UnparsedLines"/>，闸A 报出）。</summary>
    public static UsharpDomainDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var identity = "";
        var context = new List<string>();
        var sequence = new List<string>();
        var placements = new List<UsharpPlacementDecl>();
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        var retentions = new Dictionary<string, string>(StringComparer.Ordinal);
        var marks = new Dictionary<string, string>(StringComparer.Ordinal);
        var origins = new List<string>();
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        var behaviors = new Dictionary<string, string>(StringComparer.Ordinal);
        var coordinates = new List<UsharpCoordinateDecl>();
        var unparsed = new List<string>();
        var section = Section.None;

        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (UsharpLineReader.TryMarker(line, UsharpLanguage.FieldIdentity, UsharpMarkerBoundary.Whitespace, out var identityValue))
            {
                identity = identityValue;
                section = Section.None;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.FieldContext, UsharpMarkerBoundary.Whitespace, out var contextValue))
            {
                context.AddRange(UsharpLineReader.SplitList(contextValue, ','));
                section = Section.None;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.FieldSequence, UsharpMarkerBoundary.Whitespace, out var sequenceValue))
            {
                sequence.AddRange(UsharpLineReader.SplitList(sequenceValue, ','));
                section = Section.None;
                continue;
            }
            if (string.Equals(line, UsharpLanguage.FieldPlacements, StringComparison.Ordinal))
            {
                section = Section.Placements;
                continue;
            }
            if (string.Equals(line, UsharpLanguage.FieldSelections, StringComparison.Ordinal))
            {
                section = Section.Selections;
                continue;
            }
            if (string.Equals(line, UsharpLanguage.FieldRetentions, StringComparison.Ordinal))
            {
                section = Section.Retentions;
                continue;
            }
            if (string.Equals(line, UsharpLanguage.FieldMarks, StringComparison.Ordinal))
            {
                section = Section.Marks;
                continue;
            }
            if (string.Equals(line, UsharpLanguage.FieldOrigins, StringComparison.Ordinal))
            {
                section = Section.Origins;
                continue;
            }
            if (string.Equals(line, UsharpLanguage.FieldTags, StringComparison.Ordinal))
            {
                section = Section.Tags;
                continue;
            }
            if (string.Equals(line, UsharpLanguage.FieldBehaviors, StringComparison.Ordinal))
            {
                section = Section.Behaviors;
                continue;
            }
            if (string.Equals(line, "coordinates", StringComparison.Ordinal))
            {
                section = Section.Coordinates;
                continue;
            }

            switch (section)
            {
                case Section.Placements when TryParsePlacement(line, out var placement):
                    placements.Add(placement!);
                    break;
                case Section.Selections when TryParsePair(line, out var selectionName, out var predicate):
                    selections[selectionName] = predicate;
                    break;
                case Section.Retentions when TryParsePair(line, out var retentionName, out var description):
                    retentions[retentionName] = description;
                    break;
                case Section.Marks when TryParsePair(line, out var markName, out var markOrder):
                    marks[markName] = markOrder;
                    break;
                case Section.Origins when TryParsePair(line, out var originName, out _):
                    origins.Add(originName);
                    break;
                case Section.Tags when TryParsePair(line, out var tagName, out var tagOrder):
                    tags[tagName] = tagOrder;
                    break;
                case Section.Behaviors when TryParsePair(line, out var behaviorName, out var behaviorExpr):
                    behaviors[behaviorName] = behaviorExpr;
                    break;
                case Section.Coordinates when TryParseCoordinate(line, out var coordinate):
                    coordinates.Add(coordinate!);
                    break;
                default:
                    unparsed.Add(line);
                    break;
            }
        }

        return new UsharpDomainDecl(
            d.Name, identity, context, sequence, placements, selections, retentions, marks, origins,
            tags, behaviors, coordinates, unparsed, d.SourceFile, d.LineNumber);
    }

    /// <summary>坐标项解析：<c>name | family=… | axis=… | canonical=…</c>（缺 family/axis ⇒ false）。</summary>
    private static bool TryParseCoordinate(string line, out UsharpCoordinateDecl? coordinate)
    {
        coordinate = null;
        var parts = UsharpLineReader.SplitParts(line);
        if (parts.Count == 0) return false;

        var name = parts[0];
        if (name.IndexOf('=') >= 0 || name.Length == 0) return false;

        var fields = UsharpLineReader.ParseFieldBag(parts, 1, StringComparer.OrdinalIgnoreCase);
        var family = UsharpLineReader.Get(fields, "family");
        var axis = UsharpLineReader.Get(fields, "axis");
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(axis)) return false;

        coordinate = new UsharpCoordinateDecl(
            name, family, axis, UsharpLineReader.Get(fields, "canonical") ?? "required");
        return true;
    }

    /// <summary>位置项解析：<c>k=v | k=v | …</c>（缺必填字段 ⇒ false，落 <see cref="UnparsedLines"/>）。</summary>
    private static bool TryParsePlacement(string line, out UsharpPlacementDecl? placement)
    {
        placement = null;
        var fields = UsharpLineReader.ParseFieldBag(
            UsharpLineReader.SplitParts(line), 0, StringComparer.OrdinalIgnoreCase);

        if (!fields.TryGetValue(UsharpLanguage.FieldId, out var id) || id.Length == 0) return false;
        if (!fields.TryGetValue(UsharpLanguage.FieldLevel, out var level) || level.Length == 0) return false;
        if (!fields.TryGetValue(UsharpLanguage.FieldCarrier, out var carrier) || carrier.Length == 0) return false;
        if (!fields.TryGetValue(UsharpLanguage.FieldKey, out var key) || key.Length == 0) return false;
        if (!fields.TryGetValue(UsharpLanguage.FieldForm, out var form) || form.Length == 0) return false;
        if (!fields.TryGetValue(UsharpLanguage.FieldRetention, out var retention) || retention.Length == 0) return false;
        if (!fields.TryGetValue(UsharpLanguage.FieldSelection, out var selection) || selection.Length == 0) return false;
        if (!fields.TryGetValue(UsharpLanguage.FieldWriter, out var writer) || writer.Length == 0) return false;

        fields.TryGetValue(UsharpLanguage.FieldState, out var state);
        fields.TryGetValue("record-type", out var recordType);
        fields.TryGetValue("thread-coordinate", out var threadCoordinate);
        fields.TryGetValue("payload-schema", out var payloadSchema);
        fields.TryGetValue("projection", out var projection);
        placement = new UsharpPlacementDecl(
            id, level, carrier, key, form, retention, selection, writer,
            string.IsNullOrWhiteSpace(state) ? null : state,
            string.IsNullOrWhiteSpace(recordType) ? null : recordType,
            string.IsNullOrWhiteSpace(threadCoordinate) ? null : threadCoordinate,
            string.IsNullOrWhiteSpace(payloadSchema) ? null : payloadSchema,
            string.IsNullOrWhiteSpace(projection) ? null : projection);
        return true;
    }

    /// <summary>词表项解析：<c>&lt;名&gt; = &lt;值&gt;</c>（首个 <c>=</c> 切分；值剥离成对双引号）。</summary>
    private static bool TryParsePair(string line, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;
        var bag = StrictKeyValueArguments.Parse(new[] { line }, skip: 0, comparer: StringComparer.Ordinal);
        if (bag.Fields.Count == 0) return false;

        var pair = bag.Fields.First();
        name = pair.Key;
        value = pair.Value;
        if (name.Length == 0 || value.Length == 0) return false;

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }
        return true;
    }
}
