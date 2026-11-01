using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// 一条 kind 塑形声明（<c>[Gather]</c> 的 <c>shaping</c> 行）——Kind + 字段袋。
/// <para>
/// 字段袋为**有序列表**（键序 = 声明序）⇒ 发射产物逐字段含键序等价既有 JSON；键名与 JSON 键名同形
/// （<c>family</c>/<c>titleKey</c>/<c>summary</c>/<c>badge</c>/<c>timeKey</c>/<c>shaped</c>）。
/// </para>
/// </summary>
public sealed record UsharpGatherShaping(
    string Kind,
    IReadOnlyList<KeyValuePair<string, string>> Fields)
{
    /// <summary>取字段值（缺省 ⇒ null）。</summary>
    public string? Field(string key)
    {
        foreach (var pair in Fields)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>族名（<c>family</c> 字段；缺省 null）。</summary>
    public string? Family => Field(UsharpLanguage.FieldFamily);

    /// <summary>summary 字段（逗号切分；缺省空表）。</summary>
    public IReadOnlyList<string> SummaryList
    {
        get
        {
            var raw = Field(UsharpLanguage.FieldSummary);
            return string.IsNullOrWhiteSpace(raw)
                ? Array.Empty<string>()
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}

/// <summary>
/// <c>[Gather]</c> 的**读轴段**（键集取得方式的数据声明；未声明 ⇒ null = 容器轴默认）。
/// <para>
/// 语义：<c>scope</c> 层级锚点 / <c>source</c> 取得方式 / <c>edge</c> 边类型（source=edge 时必填）/
/// <c>normalize</c> 归一实现名 / <c>order</c> 序实现名 / <c>empty</c> 合法空因词。
/// 全部为受控词，闸A 校验；消费方（NodeGatherSpec）只读，不解释。
/// </para>
/// </summary>
public sealed record UsharpGatherRead(
    string Scope,
    string Source,
    string? Edge,
    string? Normalize,
    string? Order,
    string? Empty,
    string? Adapter = null,
    string? KeyFamily = null,
    string? Selection = null,
    string? Domain = null);

/// <summary>
/// <c>[Gather]</c> 的**投影段**（读后加工：折叠 + 窗口；未声明 ⇒ null = 原样输出，零回归）。
/// <para>
/// 语义：<c>identity</c> = 折叠身份键字段集（同一组字段值 ⇒ 同一事实）；<c>collapse</c> = 折叠策略
/// （<c>none|first|last|count</c>，<c>count</c> 保留计数不丢行）；<c>window</c> = 单组行数上限（&lt;=0/未声明 ⇒ 不限）。
/// </para>
/// </summary>
/// <param name="Identity">折叠身份键字段集（声明序）。</param>
/// <param name="Collapse">折叠策略受控词（缺省 none）。</param>
/// <param name="Window">单组行数上限（可空 = 不限）。</param>
public sealed record UsharpGatherProjection(
    IReadOnlyList<string> Identity,
    string Collapse,
    int? Window);

/// <summary>
/// <c>[Gather]</c> 的**呈现身份段**（读哪些帧 / 终答身份策略 / 附加气泡锚点；未声明 ⇒ null = 消费方缺省）。
/// <para>
/// 存在理由（SingleImplementation）：此前「谁算气泡 / 谁是终答」由渲染层的两处启发式
/// （本轮最后一条文本提升、role+kind 固定规则）与帧 selection 的 kind 集合各自表达；
/// 本段把它们收敛为**声明一处**，消费方只做词→枚举映射与机械匹配。
/// </para>
/// </summary>
/// <param name="FrameSelection">帧级选择名（须在域声明 <c>selections</c> 中登记）；空 = 全量帧。</param>
/// <param name="TerminalPromotion">终答身份策略受控词（<c>none|last-trace-in-round|entity-only</c>）。</param>
/// <param name="BubbleAnchors">附加气泡锚点规则（<c>role:kind</c> 形式，可多条）。</param>
public sealed record UsharpGatherPresentation(
    string? FrameSelection,
    string? TerminalPromotion,
    IReadOnlyList<string> BubbleAnchors);

/// <summary>
/// <c>[Gather] Name</c> 的语法视图（收集端口声明：族 → kind 集 + kind 塑形词表；U2）。
/// <para>
/// 书写形态（**扁平续行**，复用 U1 通道，零第二表层风格）：头 = <c>[Gather] &lt;domainId&gt;</c>；
/// 续行 = <c>kind-field &lt;字段名&gt;</c> / <c>group-by &lt;字段名&gt;</c>（可选）/
/// <c>family &lt;族名&gt; = k1, k2</c>（**行序即族序**，等价 JSON 键序）/
/// <c>shaping &lt;kind&gt; | k=v | …</c>（字段沿用 JSON 键名；<c>summary</c> 逗号列表）。
/// </para>
/// </summary>
public sealed record UsharpGatherDecl(
    string DomainId,
    string KindField,
    string? GroupBy,
    IReadOnlyList<string> FamilyOrder,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Families,
    IReadOnlyList<string> DuplicateFamilies,
    IReadOnlyList<UsharpGatherShaping> Shapings,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber,
    UsharpGatherRead? Read = null,
    UsharpGatherProjection? Projection = null,
    UsharpGatherPresentation? Presentation = null)
{
    /// <summary>附加气泡锚点（未声明 ⇒ 空表；消费方零特判）。</summary>
    public IReadOnlyList<string> BubbleAnchors =>
        Presentation?.BubbleAnchors ?? Array.Empty<string>();

    /// <summary>投影单条 <c>[Gather]</c> 声明（无名 ⇒ null；无法解析的续行落入 <see cref="UnparsedLines"/>，闸A 报出）。</summary>
    public static UsharpGatherDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var kindField = "";
        string? groupBy = null;
        var familyOrder = new List<string>();
        var families = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var duplicateFamilies = new List<string>();
        var shapings = new List<UsharpGatherShaping>();
        var unparsed = new List<string>();
        string? readScope = null;
        string? readSource = null;
        string? readEdge = null;
        string? readNormalize = null;
        string? readOrder = null;
        string? readEmpty = null;
        string? readAdapter = null;
        string? readKeyFamily = null;
        string? readSelection = null;
        string? readDomain = null;
        var hasReadMarker = false;
        var identityKeys = new List<string>();
        string? collapseMode = null;
        int? windowSize = null;
        var hasProjectionMarker = false;
        string? frameSelection = null;
        string? terminalPromotion = null;
        var bubbleAnchors = new List<string>();
        var hasPresentationMarker = false;

        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerKindField, UsharpMarkerBoundary.WhitespaceExactLine, out var kindFieldValue))
            {
                kindField = kindFieldValue;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerGroupBy, UsharpMarkerBoundary.WhitespaceExactLine, out var groupByValue))
            {
                groupBy = string.IsNullOrWhiteSpace(groupByValue) ? null : groupByValue;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadScope, UsharpMarkerBoundary.WhitespaceExactLine, out var readScopeValue))
            {
                readScope = readScopeValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadSource, UsharpMarkerBoundary.WhitespaceExactLine, out var readSourceValue))
            {
                readSource = readSourceValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadEdge, UsharpMarkerBoundary.WhitespaceExactLine, out var readEdgeValue))
            {
                readEdge = readEdgeValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadNormalize, UsharpMarkerBoundary.WhitespaceExactLine, out var readNormalizeValue))
            {
                readNormalize = readNormalizeValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadOrder, UsharpMarkerBoundary.WhitespaceExactLine, out var readOrderValue))
            {
                readOrder = readOrderValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadEmpty, UsharpMarkerBoundary.WhitespaceExactLine, out var readEmptyValue))
            {
                readEmpty = readEmptyValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadAdapter, UsharpMarkerBoundary.WhitespaceExactLine, out var readAdapterValue))
            {
                readAdapter = readAdapterValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadKeyFamily, UsharpMarkerBoundary.WhitespaceExactLine, out var readKeyFamilyValue))
            {
                readKeyFamily = readKeyFamilyValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadSelection, UsharpMarkerBoundary.WhitespaceExactLine, out var readSelectionValue))
            {
                readSelection = readSelectionValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerReadDomain, UsharpMarkerBoundary.WhitespaceExactLine, out var readDomainValue))
            {
                readDomain = readDomainValue;
                hasReadMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerIdentity, UsharpMarkerBoundary.WhitespaceExactLine, out var identityValue))
            {
                identityKeys.Clear();
                foreach (var part in identityValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    identityKeys.Add(part);
                }
                hasProjectionMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerCollapse, UsharpMarkerBoundary.WhitespaceExactLine, out var collapseValue))
            {
                collapseMode = collapseValue.Trim();
                hasProjectionMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerWindow, UsharpMarkerBoundary.WhitespaceExactLine, out var windowValue))
            {
                windowSize = int.TryParse(windowValue.Trim(), out var parsedWindow) ? parsedWindow : 0;
                hasProjectionMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerFrameSelection, UsharpMarkerBoundary.WhitespaceExactLine, out var frameSelectionValue))
            {
                frameSelection = string.IsNullOrWhiteSpace(frameSelectionValue) ? null : frameSelectionValue.Trim();
                hasPresentationMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerTerminalPromotion, UsharpMarkerBoundary.WhitespaceExactLine, out var terminalPromotionValue))
            {
                terminalPromotion = string.IsNullOrWhiteSpace(terminalPromotionValue) ? null : terminalPromotionValue.Trim();
                hasPresentationMarker = true;
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerBubbleAnchor, UsharpMarkerBoundary.WhitespaceExactLine, out var bubbleAnchorValue))
            {
                var anchor = bubbleAnchorValue.Trim();
                if (anchor.Length == 0) unparsed.Add(line);
                else bubbleAnchors.Add(anchor);
                hasPresentationMarker = true;
                continue;
            }

            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerFamily, UsharpMarkerBoundary.WhitespaceExactLine, out var familyValue))
            {
                if (TryParseFamily(familyValue, out var name, out var kinds))
                {
                    if (families.ContainsKey(name)) duplicateFamilies.Add(name);
                    else familyOrder.Add(name);
                    families[name] = kinds;
                }
                else
                {
                    unparsed.Add(line);
                }
                continue;
            }
            if (UsharpLineReader.TryMarker(line, UsharpLanguage.MarkerShaping, UsharpMarkerBoundary.WhitespaceExactLine, out var shapingValue))
            {
                if (TryParseShaping(shapingValue, out var shaping)) shapings.Add(shaping!);
                else unparsed.Add(line);
                continue;
            }

            unparsed.Add(line);
        }

        var read = hasReadMarker
            ? new UsharpGatherRead(
                readScope ?? string.Empty,
                readSource ?? string.Empty,
                UsharpLineReader.NullIfEmpty(readEdge),
                UsharpLineReader.NullIfEmpty(readNormalize),
                UsharpLineReader.NullIfEmpty(readOrder),
                UsharpLineReader.NullIfEmpty(readEmpty),
                UsharpLineReader.NullIfEmpty(readAdapter),
                UsharpLineReader.NullIfEmpty(readKeyFamily),
                UsharpLineReader.NullIfEmpty(readSelection),
                UsharpLineReader.NullIfEmpty(readDomain))
            : null;

        var projection = hasProjectionMarker
            ? new UsharpGatherProjection(
                identityKeys.ToArray(),
                string.IsNullOrWhiteSpace(collapseMode) ? "none" : collapseMode!,
                windowSize)
            : null;

        var presentation = hasPresentationMarker
            ? new UsharpGatherPresentation(
                frameSelection,
                terminalPromotion,
                bubbleAnchors.ToArray())
            : null;

        return new UsharpGatherDecl(
            d.Name, kindField, groupBy, familyOrder, families, duplicateFamilies, shapings,
            unparsed, d.SourceFile, d.LineNumber, read, projection, presentation);
    }

    /// <summary>族声明解析：<c>&lt;族名&gt; = k1, k2</c>（首 <c>=</c> 切分；kind 集可为空）。</summary>
    private static bool TryParseFamily(string value, out string name, out IReadOnlyList<string> kinds)
    {
        name = string.Empty;
        kinds = Array.Empty<string>();

        var eq = value.IndexOf('=');
        if (eq <= 0) return false;

        name = value[..eq].Trim();
        if (name.Length == 0) return false;

        kinds = value[(eq + 1)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return true;
    }

    /// <summary>塑形行解析：<c>&lt;kind&gt; | k=v | …</c>（字段袋保序）。</summary>
    private static bool TryParseShaping(string value, out UsharpGatherShaping? shaping)
    {
        shaping = null;
        var segments = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return false;

        var kind = segments[0].Trim();
        if (kind.Length == 0) return false;

        var fields = new List<KeyValuePair<string, string>>();
        for (var i = 1; i < segments.Length; i++)
        {
            var part = segments[i];
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part[..eq].Trim();
            var fieldValue = part[(eq + 1)..].Trim();
            if (key.Length == 0) continue;
            fields.Add(new KeyValuePair<string, string>(key, fieldValue));
        }

        shaping = new UsharpGatherShaping(kind, fields);
        return true;
    }
}
