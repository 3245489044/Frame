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
    int LineNumber)
{
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

        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (TryStripMarker(line, UsharpLanguage.MarkerKindField, out var kindFieldValue))
            {
                kindField = kindFieldValue;
                continue;
            }
            if (TryStripMarker(line, UsharpLanguage.MarkerGroupBy, out var groupByValue))
            {
                groupBy = string.IsNullOrWhiteSpace(groupByValue) ? null : groupByValue;
                continue;
            }
            if (TryStripMarker(line, UsharpLanguage.MarkerFamily, out var familyValue))
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
            if (TryStripMarker(line, UsharpLanguage.MarkerShaping, out var shapingValue))
            {
                if (TryParseShaping(shapingValue, out var shaping)) shapings.Add(shaping!);
                else unparsed.Add(line);
                continue;
            }

            unparsed.Add(line);
        }

        return new UsharpGatherDecl(
            d.Name, kindField, groupBy, familyOrder, families, duplicateFamilies, shapings,
            unparsed, d.SourceFile, d.LineNumber);
    }

    /// <summary>首词标记剥离（标记后须跟空白或整行即标记；防裸键误剥，同 <c>UsharpDomainDecl</c> 口径）。</summary>
    private static bool TryStripMarker(string line, string marker, out string rest)
    {
        rest = string.Empty;
        if (!line.StartsWith(marker, StringComparison.Ordinal)) return false;
        if (line.Length == marker.Length) return true;
        if (!char.IsWhiteSpace(line[marker.Length])) return false;
        rest = line[marker.Length..].Trim();
        return true;
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
