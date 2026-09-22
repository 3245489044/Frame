using System.Reflection;
using System.Text;
using System.Text.Json;
using DataCenter.Protocol.Standards;
using Plate.JsonMethod.Utilities;

namespace DataCenter.Standards;

/// <summary>六向完备性矩阵的坐标轴（判据母表的列）。</summary>
public enum CompletenessAxis
{
    /// <summary>① 词表：词是否登记在唯一 C# 源（<see cref="PortImplVocabulary"/>）。</summary>
    Vocabulary,

    /// <summary>② 结构门：词是否在 <c>schemas\*.schema.json</c> 声明（enum 成员或对象键）。</summary>
    Schema,

    /// <summary>③ 解析：标准层读取器是否**分派/校验**该词位（哨兵反证）。</summary>
    Parse,

    /// <summary>④ 标准模型：词是否有承载它的中立模型成员。</summary>
    Model,

    /// <summary>⑤ 实现：内核执行面是否有对应落地（外来证据，标准层不可见）。</summary>
    Implementation,

    /// <summary>⑥ 自证/对拍：是否有自证用例与对拍（外来证据）。</summary>
    Attestation,
}

/// <summary>单元格状态。</summary>
public enum CompletenessStatus
{
    /// <summary>已落地（探针/登记均通过）。</summary>
    Present,

    /// <summary>确定缺失（探针可判且判定为否）。</summary>
    Missing,

    /// <summary>无法判定：无探针登记 / 无外来证据（须显式补口或记豁免，**不得静默**）。</summary>
    Unregistered,
}

/// <summary>一个单元格 = 一个轴上的状态与依据。</summary>
public sealed record CompletenessCell(CompletenessAxis Axis, CompletenessStatus Status, string? Detail = null);

/// <summary>一行 = 一个「词」的六向落点。</summary>
public sealed record CompletenessRow(string Family, string Word, IReadOnlyList<CompletenessCell> Cells)
{
    /// <summary>该词是否存在任一「未落地」项（缺失 / 无法判定）。</summary>
    public bool HasGap => Cells.Any(cell => cell.Status != CompletenessStatus.Present);

    /// <summary>确定缺失的轴（差集主项：缺项清单据此分类）。</summary>
    public IReadOnlyList<CompletenessAxis> MissingAxes => Cells
        .Where(cell => cell.Status == CompletenessStatus.Missing)
        .Select(cell => cell.Axis)
        .ToArray();

    /// <summary>无法判定的轴（须补探针或补证据）。</summary>
    public IReadOnlyList<CompletenessAxis> UnregisteredAxes => Cells
        .Where(cell => cell.Status == CompletenessStatus.Unregistered)
        .Select(cell => cell.Axis)
        .ToArray();
}

/// <summary>
/// 六向完备性报告（矩阵 + 差集）。纯数据；<see cref="Gaps"/> 即「缺项清单 = 待办清单」。
/// </summary>
public sealed record CompletenessReport(IReadOnlyList<CompletenessRow> Rows, string? Note = null)
{
    /// <summary>家族名（登记序，去重）。</summary>
    public IReadOnlyList<string> Families => Rows.Select(row => row.Family).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>词总数。</summary>
    public int WordCount => Rows.Count;

    /// <summary>差集（任一轴未落地的词）。</summary>
    public IReadOnlyList<CompletenessRow> Gaps => Rows.Where(row => row.HasGap).ToArray();

    /// <summary>确定缺失项计数（按轴）。</summary>
    public IReadOnlyDictionary<CompletenessAxis, int> MissingCounts => Count(CompletenessStatus.Missing);

    /// <summary>无法判定项计数（按轴）；非零即「探针/证据未登记」待办。</summary>
    public IReadOnlyDictionary<CompletenessAxis, int> UnregisteredCounts => Count(CompletenessStatus.Unregistered);

    private IReadOnlyDictionary<CompletenessAxis, int> Count(CompletenessStatus status)
        => Rows.SelectMany(row => row.Cells)
            .Where(cell => cell.Status == status)
            .GroupBy(cell => cell.Axis)
            .ToDictionary(group => group.Key, group => group.Count());

    /// <summary>文本矩阵（基线包落盘与人工复核用；列序 = 轴序）。</summary>
    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("家族\t词\t" + string.Join('\t', Enum.GetNames<CompletenessAxis>()));
        foreach (var row in Rows)
            builder.AppendLine(
                row.Family + "\t" + row.Word + "\t"
                + string.Join('\t', row.Cells.Select(cell => cell.Status switch
                {
                    CompletenessStatus.Present => "OK",
                    CompletenessStatus.Missing => "MISS",
                    _ => "UNREG",
                })));

        return builder.ToString();
    }

    /// <summary>差集清单（人读形态：家族 / 词 / 缺失轴 / 依据）。</summary>
    public string RenderGaps()
    {
        var builder = new StringBuilder();
        foreach (var row in Gaps)
            foreach (var cell in row.Cells.Where(cell => cell.Status != CompletenessStatus.Present))
                builder.AppendLine($"{row.Family}/{row.Word}\t{cell.Axis}\t{cell.Status}\t{cell.Detail}");

        return builder.ToString();
    }
}

/// <summary>
/// 外来证据（⑤ 实现 / ⑥ 自证对拍）：标准层**不可见**的两向由内核 / 测试侧显式登记。
/// <para>键 = <c>家族/词</c>（见 <see cref="Key"/>）；未提供证据 ⇒ 两向记 <see cref="CompletenessStatus.Unregistered"/>（不产生假绿）。</para>
/// </summary>
/// <param name="ExternalFamilies">标准层词表不可见的外来族（族名 → 词集，如内核 <c>PortOp</c> 操作码）。</param>
/// <param name="Implemented">已落地的「家族/词」。</param>
/// <param name="Attested">有自证/对拍的「家族/词」。</param>
public sealed record StandardCompletenessEvidence(
    IReadOnlyDictionary<string, IReadOnlyList<string>> ExternalFamilies,
    IReadOnlySet<string> Implemented,
    IReadOnlySet<string> Attested)
{
    /// <summary>空证据（⑤/⑥ 全记 Unregistered）。</summary>
    public static StandardCompletenessEvidence Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    /// <summary>证据键（家族 + 词）。</summary>
    public static string Key(string family, string word) => family + "/" + word;
}

/// <summary>
/// 六向完备性矩阵生成器（F0 判据母表）：把一个「词」的六个落点逐项**机械判定**并输出差集。
/// <para>
/// 职责边界（红线）：本件**只读报告**（不写盘、不阻断）；⑤/⑥ 由外来证据供给（标准层不引执行面）。
/// ①–④ 的数据源全部取自在册单源：<see cref="PortImplVocabulary"/>（词表）、
/// <see cref="SchemaEnumSynchronizer.EnumBindings"/>（结构门绑定指针）、<see cref="StandardJson"/>（读取器）、标准模型反射。
/// </para>
/// <para>
/// 探针纪律（自证）：③ 向探针先以该族**合法词**验证「读取器接受」，再以哨兵值验证「读取器拒绝」；
/// 合法词验证失败即记 <see cref="CompletenessStatus.Unregistered"/> 并附读取器原文（不产生假绿）。
/// </para>
/// </summary>
public static class StandardCompleteness
{
    private const string Sentinel = "__completeness_sentinel__";
    private const string PortsSchema = "ports.schema.json";

    /// <summary>构建矩阵（只读）；<paramref name="evidence"/> 为 null ⇒ ⑤/⑥ 记 Unregistered。</summary>
    public static CompletenessReport Build(StandardCompletenessEvidence? evidence = null)
    {
        var rows = new List<CompletenessRow>();
        foreach (var family in FamilyTable) rows.AddRange(BuildRows(family, evidence));
        if (evidence is not null)
            foreach (var (name, words) in evidence.ExternalFamilies)
                foreach (var word in words)
                    rows.Add(new CompletenessRow(name, word,
                    [
                        External(CompletenessAxis.Vocabulary),
                        External(CompletenessAxis.Schema),
                        External(CompletenessAxis.Parse),
                        External(CompletenessAxis.Model),
                        EvidenceCell(CompletenessAxis.Implementation, name, word, evidence),
                        EvidenceCell(CompletenessAxis.Attestation, name, word, evidence),
                    ]));

        return new CompletenessReport(rows);
    }

    /// <summary>已登记家族名（登记序）；外来族不在其列。</summary>
    public static IReadOnlyList<string> FamilyNames => FamilyTable.Select(family => family.Name).ToArray();

    /// <summary>
    /// 词的落点投影（L3 关系图的**唯一取数口**：家族登记表不另抄一份，见 <see cref="StandardRelations"/>）。
    /// </summary>
    /// <param name="SchemaFile">结构门文件名。</param>
    /// <param name="SchemaPointer">结构门指针（null = 未登记落点）。</param>
    /// <param name="ModelType">中立模型类型名（null = 未登记模型落点）。</param>
    /// <param name="ModelMember">模型成员名（null = 未登记模型落点）。</param>
    /// <param name="HasReaderProbe">是否登记了③ 向解析探针。</param>
    public sealed record CompletenessLanding(
        string Family,
        string Word,
        string SchemaFile,
        string? SchemaPointer,
        string? ModelType,
        string? ModelMember,
        bool HasReaderProbe);

    /// <summary>全部词的落点投影（登记序；词源 = 家族登记表，禁手抄）。</summary>
    public static IReadOnlyList<CompletenessLanding> Landings
    {
        get
        {
            var landings = new List<CompletenessLanding>();
            foreach (var family in FamilyTable)
                foreach (var word in family.Words)
                    landings.Add(new CompletenessLanding(
                        family.Name,
                        word,
                        family.SchemaFile,
                        family.SchemaPointer,
                        family.ModelType?.Name,
                        family.ModelMember?.Invoke(word),
                        family.WordProbe is not null));

            return landings;
        }
    }

    /// <summary>家族描述：词源 + 六向落点（探针 / 指针 / 模型成员）。</summary>
    private sealed record Family(
        string Name,
        IReadOnlyList<string> Words,
        string? SchemaPointer,
        Func<string, IReadOnlyList<string>?>? SchemaWords,
        Func<string, string>? WordProbe,
        string? FormProbe,
        Type? ModelType,
        Func<string, string?>? ModelMember,
        string SchemaFile = PortsSchema);

    /// <summary>
    /// 家族登记表（**唯一登记点**）：词源一律取自词表本身（禁手抄），新增词表 / 新增词在本表可见范围内。
    /// </summary>
    private static readonly Family[] FamilyTable =
    [
        new("impl.kind", PortImplVocabulary.Kinds, "$defs/impl/properties/kind/enum", null,
            KindProbe, KindProbe(Sentinel), typeof(PortImplStandard), _ => nameof(PortImplStandard.Kind)),
        new("convert", PortImplVocabulary.Conversions, "$defs/arg/properties/convert/enum", null,
            ConvertProbe, ConvertProbe(Sentinel), typeof(PortArgStandard), _ => nameof(PortArgStandard.Convert)),
        new("from", PortImplVocabulary.ValueSourceFromAll, "$defs/valueSource/properties/from/enum", null,
            FromProbe, FromProbe(Sentinel), typeof(ValueSourceStandard), _ => nameof(ValueSourceStandard.From)),
        new("select", PortImplVocabulary.Selects, "$defs/composedField/properties/select/enum", null,
            SelectProbe, SelectProbe(Sentinel), typeof(ComposedFieldStandard), _ => nameof(ComposedFieldStandard.Select)),
        new("deliver.kind", PortImplVocabulary.DeliverKinds, "$defs/deliver/properties/kind/enum", null,
            DeliverProbe, DeliverProbe(Sentinel), typeof(DeliverStandard), _ => nameof(DeliverStandard.Kind)),
        new("onBlankFirstKey", PortImplVocabulary.OnBlankFirstKey, "$defs/valueSource/properties/onBlankFirstKey/enum",
            null,
            word => TableProbe("onBlankFirstKey", word), TableProbe("onBlankFirstKey", Sentinel),
            typeof(ValueSourceStandard), _ => nameof(ValueSourceStandard.OnBlankFirstKey)),
        new("onDuplicate", PortImplVocabulary.OnDuplicate, "$defs/valueSource/properties/onDuplicate/enum",
            null,
            word => TableProbe("onDuplicate", word), TableProbe("onDuplicate", Sentinel),
            typeof(ValueSourceStandard), _ => nameof(ValueSourceStandard.OnDuplicate)),
        new("aggOp", PortImplVocabulary.AggOps, "$defs/expr/properties/aggOp/enum", null,
            AggOpProbe, AggOpProbe(Sentinel), typeof(ExprStandard), _ => nameof(ExprStandard.AggOp)),
        // 指针修正（F2-b 收口）：deliver.capture 是 $ref，嵌套指针不可达；消息口径的实节点在 $defs/capture。
        new("message", PortImplVocabulary.MessageModes, "$defs/capture/properties/message/enum", null,
            MessageProbe, MessageProbe(Sentinel), typeof(DeliverCaptureStandard), _ => nameof(DeliverCaptureStandard.Message)),
        new("expr", PortImplVocabulary.ExprSlots.Select(slot => slot.Member).ToArray(), "$defs/expr/properties",
            ExprSchemaWords, null, null, typeof(ExprStandard), member => member),
        new("scope.kind", PortImplVocabulary.ScopeKinds, "$defs/scope/properties/kind/enum", null,
            ScopeProbe, ScopeProbe(Sentinel), typeof(ScopeStandard), _ => nameof(ScopeStandard.Kind)),
        new("fake", PortImplVocabulary.FakeKinds, "$defs/dep/properties/fake/enum", null,
            FakeProbe, FakeProbe(Sentinel), typeof(DepStandard), _ => nameof(DepStandard.Fake)),
        new("dep.form", PortImplVocabulary.DepForms, "$defs/dep/properties", word => [word],
            DepFormProbe, DepFormSentinel, typeof(DepStandard), DepFormMember),
    ];

    // ── ③ 解析探针：最小可读 impl 信封 + 词位（哨兵 = 未登记值，用于反证「读取器是否分派」） ──
    private static string Envelope(string implBody) =>
        "{\"ports\":{\"P.K\":{\"scope\":\"Lease\",\"impl\":" + implBody + "}}}";

    /// <summary>
    /// SK-28 起 <c>impl.kind</c> **必填**（缺省已取消）：未自带 <c>kind</c> 的词位探针一律经本件补
    /// <paramref name="kind"/>（缺省 <c>reflective</c>），否则读取器在进入词位分派前即抛「impl.kind 必填」。
    /// </summary>
    /// <param name="implFragment">impl 对象体的**余部**（逗号起首、已含闭合 <c>}</c>，如 <c>,"args":[…]}</c>）。</param>
    /// <param name="kind">补入的绑定方式。</param>
    private static string EnvelopeWithKind(string implFragment, string kind = "reflective") =>
        Envelope("{\"kind\":\"" + kind + "\"" + implFragment);

    private static string Quoted(string value) => "\"" + value + "\"";

    private static string KindProbe(string word) => Envelope("{\"kind\":" + Quoted(word) + "}");

    private static string ConvertProbe(string word) =>
        EnvelopeWithKind(",\"args\":[{\"convert\":" + Quoted(word) + "}]}");

    /// <summary>
    /// <c>from</c> 词位探针：<c>table</c>/<c>presence</c> 必须带 <c>rows</c>/<c>items</c> 来源 —— 否则读取器的
    /// 「缺来源」分支对 <see cref="JsonElement"/>（值类型）恒不可达，会自递归（既有登记缺陷，见 <c>RowSource</c> 注记）。
    /// <c>list</c>/<c>map</c> 为集合字面量原语：<c>items</c> 必须是元素值源**数组**、<c>pairs</c> 必须是 <c>[键,值]</c> 数组序列。
    /// </summary>
    private static string FromProbe(string word) => word switch
    {
        "list" => Envelope("{\"kind\":\"scripted\",\"bind\":{\"s\":{\"from\":\"list\","
                   + "\"elementType\":\"System.String\",\"items\":[{\"literal\":\"a\"}]}}}"),
        "map" => Envelope("{\"kind\":\"scripted\",\"bind\":{\"s\":{\"from\":\"map\","
                   + "\"genericArgs\":[\"System.String\",\"System.String\"],"
                   + "\"pairs\":[[{\"literal\":\"k\"},{\"literal\":\"v\"}]]}}}"),
        _ => Envelope("{\"kind\":\"scripted\",\"bind\":{\"s\":{\"from\":" + Quoted(word)
                      + ",\"rows\":{\"from\":\"with\",\"key\":\"pairs\"}"
                      + ",\"items\":{\"from\":\"with\",\"key\":\"tokens\"}}}}"),
    };

    private static string SelectProbe(string word) =>
        Envelope("{\"kind\":\"composed\",\"project\":[{\"field\":\"f\",\"call\":\"c\",\"select\":" + Quoted(word) + "}]}");

    /// <summary><c>aggOp</c> 词位探针（<c>deliver.value</c> 表达式位：算子键仅在表达式层合法，<c>bind</c> 层只认取值来源键）。</summary>
    private static string AggOpProbe(string word) =>
        Envelope("{\"kind\":\"scripted\",\"deliver\":{\"kind\":\"value\",\"value\":"
                 + "{\"agg\":{\"from\":\"with\",\"key\":\"xs\"},\"aggOp\":" + Quoted(word) + "}}}");

    /// <summary><c>message</c> 词位探针（<c>deliver.capture.message</c> 消息口径）。</summary>
    private static string MessageProbe(string word) =>
        Envelope("{\"kind\":\"scripted\",\"deliver\":{\"kind\":\"issues\",\"capture\":"
                 + "{\"call\":{\"target\":\"T\",\"method\":\"M\"},\"expect\":[\"System.Exception\"],"
                 + "\"message\":" + Quoted(word) + "}}}");

    private static string DeliverProbe(string word) =>
        Envelope("{\"kind\":\"scripted\",\"deliver\":{\"kind\":" + Quoted(word) + "}}");

    private static string ScopeProbe(string word) =>
        Envelope("{\"kind\":\"scripted\",\"scopes\":[{\"kind\":" + Quoted(word) + ",\"as\":\"s\"}]}");

    private static string FakeProbe(string word) =>
        EnvelopeWithKind(",\"deps\":[{\"fake\":" + Quoted(word) + ",\"contract\":\"I\"}]}");

    /// <summary>表单形态位哨兵：<c>deps[]</c> 未声明 <c>new</c>/<c>factory</c>/<c>fake</c> 即抛。</summary>
    private static string DepFormSentinel =>
        EnvelopeWithKind(",\"deps\":[{\"" + Sentinel + "\":1}]}");

    private static string DepFormProbe(string word) => word switch
    {
        "new" => EnvelopeWithKind(",\"deps\":[{\"new\":\"T\"}]}"),
        "factory" => EnvelopeWithKind(",\"deps\":[{\"factory\":\"f\"}]}"),
        "fake" => EnvelopeWithKind(",\"deps\":[{\"fake\":\"recording\",\"contract\":\"I\"}]}"),
        _ => EnvelopeWithKind(",\"deps\":[{\"new\":\"T\"}]}"),
    };

    private static string? DepFormMember(string word) => word switch
    {
        "new" => nameof(DepStandard.New),
        "factory" => nameof(DepStandard.Factory),
        "fake" => nameof(DepStandard.Fake),
        _ => null,
    };

    /// <summary>行表处置词位探针（<c>table</c> 取值来源的 <c>onBlankFirstKey</c> / <c>onDuplicate</c>）。</summary>
    private static string TableProbe(string key, string word) =>
        Envelope("{\"kind\":\"scripted\",\"bind\":{\"s\":{\"from\":\"table\","
                 + "\"rows\":{\"from\":\"with\",\"key\":\"pairs\"}," + Quoted(key) + ":" + Quoted(word) + "}}}");

    /// <summary>
    /// 表达式成员承载的结构门键（平铺键 + 宿主对象内嵌套子键；由 <c>ExprSlots</c> 登记派生）。
    /// 未登记任何键 ⇒ null（= 词表登记待补，矩阵记 Unregistered）。
    /// </summary>
    private static IReadOnlyList<string>? ExprSchemaWords(string member)
    {
        foreach (var slot in PortImplVocabulary.ExprSlots)
            if (string.Equals(slot.Member, member, StringComparison.Ordinal))
                return slot.DeclaredKeys.Any() ? slot.DeclaredKeys.ToArray() : null;

        return null;
    }

    /// <summary>单族行集：家族级探针一次求值、逐词共享；词级探针逐词自证（失败不产生假绿）。</summary>
    private static IEnumerable<CompletenessRow> BuildRows(Family family, StandardCompletenessEvidence? evidence)
    {
        var declared = family.SchemaPointer is null
            ? (Exists: false, Members: (IReadOnlyList<string>?)null)
            : ReadSchemaNode(SchemaText(family.SchemaFile), family.SchemaPointer);
        var bound = family.SchemaPointer is not null && IsVocabularyBound(family.SchemaFile, family.SchemaPointer);
        var parse = EvaluateParse(family);

        foreach (var word in family.Words)
        {
            var schemaWords = family.SchemaWords is null ? (IReadOnlyList<string>?)[word] : family.SchemaWords(word);
            yield return new CompletenessRow(family.Name, word,
            [
                new CompletenessCell(CompletenessAxis.Vocabulary, CompletenessStatus.Present, "词表唯一源"),
                SchemaCell(declared, bound, schemaWords, family.SchemaPointer),
                parse[word],
                ModelCell(family, word),
                EvidenceCell(CompletenessAxis.Implementation, family.Name, word, evidence),
                EvidenceCell(CompletenessAxis.Attestation, family.Name, word, evidence),
            ]);
        }
    }

    /// <summary>
    /// ③ 向求值（探针纪律）：家族级哨兵反证（读取器拒绝哨兵 ⇒ 该词位有分派/校验）+ 词级自证（读取器须接受该词）。
    /// 无探针登记 ⇒ 全词 Unregistered（显式待办，不产生假绿）。
    /// </summary>
    private static IReadOnlyDictionary<string, CompletenessCell> EvaluateParse(Family family)
    {
        var cells = new Dictionary<string, CompletenessCell>(StringComparer.Ordinal);
        if (family.WordProbe is null)
        {
            foreach (var word in family.Words)
                cells[word] = new CompletenessCell(CompletenessAxis.Parse, CompletenessStatus.Unregistered, "未登记探针");

            return cells;
        }

        var dispatched = !TryRead(family.FormProbe ?? family.WordProbe(Sentinel), out _);
        var schemaCarries = family.SchemaPointer is not null
                            && IsVocabularyBound(family.SchemaFile, family.SchemaPointer);
        foreach (var word in family.Words)
        {
            cells[word] = TryRead(family.WordProbe(word), out var error)
                ? new CompletenessCell(
                    CompletenessAxis.Parse,
                    dispatched ? CompletenessStatus.Present : CompletenessStatus.Missing,
                    dispatched
                        ? "读取器分派 / 校验该词位"
                        : schemaCarries
                            ? "无读取器分派（承担方 = 结构门 enum 绑定）"
                            : "无读取器分派且结构门无 enum 绑定（真缺口）")
                : new CompletenessCell(
                    CompletenessAxis.Parse,
                    CompletenessStatus.Missing,
                    "读取器拒绝该词：" + FirstLine(error));
        }

        return cells;
    }

    /// <summary>② 向求值：结构门声明（enum 成员 / 对象键；成员可承载多个键）+ 是否有词表派生绑定。</summary>
    private static CompletenessCell SchemaCell(
        (bool Exists, IReadOnlyList<string>? Members) node,
        bool bound,
        IReadOnlyList<string>? words,
        string? pointer)
    {
        if (pointer is null)
            return new CompletenessCell(CompletenessAxis.Schema, CompletenessStatus.Unregistered, "未登记结构门落点");
        if (!node.Exists)
            return new CompletenessCell(CompletenessAxis.Schema, CompletenessStatus.Missing, $"结构门无该节点：{pointer}");
        if (node.Members is null)
            return new CompletenessCell(CompletenessAxis.Schema, CompletenessStatus.Missing,
                $"结构门无 enum / 键集声明：{pointer}");
        if (words is null)
            return new CompletenessCell(CompletenessAxis.Schema, CompletenessStatus.Unregistered,
                "该成员未登记承载键（词表登记待补）");

        var absent = words.Where(word => !node.Members.Contains(word, StringComparer.Ordinal)).ToArray();
        var suffix = bound ? "（词表派生绑定）" : "（无词表派生绑定）";
        return absent.Length == 0
            ? new CompletenessCell(CompletenessAxis.Schema, CompletenessStatus.Present, "已声明" + suffix)
            : new CompletenessCell(CompletenessAxis.Schema, CompletenessStatus.Missing,
                "未声明 " + string.Join(",", absent) + suffix);
    }

    /// <summary>④ 向求值：词是否有承载它的中立模型成员（反射；缺成员即缺落点）。</summary>
    private static CompletenessCell ModelCell(Family family, string word)
    {
        if (family.ModelType is null || family.ModelMember is null)
            return new CompletenessCell(CompletenessAxis.Model, CompletenessStatus.Unregistered, "未登记模型落点");

        var member = family.ModelMember(word);
        if (member is null)
            return new CompletenessCell(CompletenessAxis.Model, CompletenessStatus.Unregistered, "未登记模型落点");

        return family.ModelType.GetProperty(member, BindingFlags.Public | BindingFlags.Instance) is not null
            ? new CompletenessCell(CompletenessAxis.Model, CompletenessStatus.Present, $"{family.ModelType.Name}.{member}")
            : new CompletenessCell(CompletenessAxis.Model, CompletenessStatus.Missing, $"缺 {family.ModelType.Name}.{member}");
    }

    /// <summary>⑤/⑥ 向求值：外来证据登记（缺失即 Missing，未提供证据即 Unregistered）。</summary>
    private static CompletenessCell EvidenceCell(
        CompletenessAxis axis, string family, string word, StandardCompletenessEvidence? evidence)
    {
        if (evidence is null)
            return new CompletenessCell(axis, CompletenessStatus.Unregistered, "未提供外来证据");

        var key = StandardCompletenessEvidence.Key(family, word);
        var registered = axis == CompletenessAxis.Implementation ? evidence.Implemented : evidence.Attested;
        return registered.Contains(key)
            ? new CompletenessCell(axis, CompletenessStatus.Present, key)
            : new CompletenessCell(axis, CompletenessStatus.Missing, "未登记：" + key);
    }

    /// <summary>外来族（标准层词表不可见）的 ①–④ = Unregistered（显式：不冒充已落地）。</summary>
    private static CompletenessCell External(CompletenessAxis axis)
        => new(axis, CompletenessStatus.Unregistered, "标准层不可见（外来词表）");

    /// <summary>读取器自证：能读通 → true；读不通 → false 并给出首行诊断。</summary>
    private static bool TryRead(string portsJson, out string error)
    {
        try
        {
            _ = StandardJson.ReadPorts(portsJson);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>结构门节点探针（关系图共用**同一实现**；禁第二份指针解析）。</summary>
    internal static (bool Exists, IReadOnlyList<string>? Members) SchemaNode(string schemaFile, string pointer)
        => ReadSchemaNode(SchemaText(schemaFile), pointer);

    /// <summary>
    /// 结构门节点读取：返回「节点是否存在」与「可校验成员」（<c>enum</c> 数组值 / <c>properties</c> 对象键集）；
    /// 节点存在但既无 enum 也无键集 ⇒ 成员为 null（= 结构门对该词位无可校验形态，属真缺口）。
    /// </summary>
    private static (bool Exists, IReadOnlyList<string>? Members) ReadSchemaNode(string schemaText, string pointer)
    {
        using var document = JsonDocument.Parse(schemaText, JsonTolerantReader.Tolerant);
        var node = document.RootElement;
        foreach (var token in pointer.Split('/'))
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(token, out var next)) return (false, null);
            node = next;
        }

        if (node.ValueKind == JsonValueKind.Array)
            return (true, node.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .ToArray());

        if (node.ValueKind != JsonValueKind.Object) return (true, null);
        if (node.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
            return (true, values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .ToArray());

        return node.EnumerateObject().All(property => property.Value.ValueKind == JsonValueKind.Object)
            ? (true, node.EnumerateObject().Select(property => property.Name).ToArray())
            : (true, null);
    }

    /// <summary>结构门文本（内嵌单一源；文件名与 <c>schemas\</c> 下物理文件同名）。</summary>
    private static string SchemaText(string fileName) => fileName switch
    {
        "ports.schema.json" => StandardSchemas.Ports,
        "events.schema.json" => StandardSchemas.Events,
        "fakes.schema.json" => StandardSchemas.Fakes,
        "data.schema.json" => StandardSchemas.Data,
        _ => throw new StandardViolationException($"未登记的结构门文件 '{fileName}'（见 StandardSchemas）"),
    };

    /// <summary>该结构门落点是否有**词表派生绑定**（指针表唯一源 = <see cref="SchemaEnumSynchronizer"/>）。</summary>
    private static bool IsVocabularyBound(string schemaFile, string pointer)
        => SchemaEnumSynchronizer.EnumBindings.Any(binding =>
               string.Equals(binding.SchemaFile, schemaFile, StringComparison.Ordinal)
               && string.Equals(binding.Pointer, pointer, StringComparison.Ordinal))
           || SchemaEnumSynchronizer.PropertiesKeyBindings.Any(binding =>
               string.Equals(binding.SchemaFile, schemaFile, StringComparison.Ordinal)
               && string.Equals(binding.Pointer, pointer, StringComparison.Ordinal));

    /// <summary>诊断首行（矩阵单元格为单行文本）。</summary>
    private static string FirstLine(string message)
    {
        var index = message.IndexOfAny(['\r', '\n']);
        return index < 0 ? message : message[..index];
    }
}


