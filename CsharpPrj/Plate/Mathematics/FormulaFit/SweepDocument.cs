using System.Text.Json;

namespace MathematicalAlgorithms.FormulaFit;

/// <summary>sweep 文档：schema 校验 + 点集（JSON 元素已 Clone，脱离文档生命周期）。</summary>
public sealed record SweepDocument(
    string SourcePath,
    string Schema,
    IReadOnlyList<SweepPoint> Points)
{
    public const string CurrentSchema = "gameeditor-sweep-v1";

    public static SweepDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"sweep 根必须是对象：{path}");

        string schema = root.TryGetProperty("schema", out JsonElement schemaElement)
            && schemaElement.ValueKind == JsonValueKind.String
            ? schemaElement.GetString() ?? string.Empty
            : string.Empty;

        if (!string.Equals(schema, CurrentSchema, StringComparison.Ordinal))
            throw new InvalidOperationException($"sweep schema 不匹配：{schema}，期望 {CurrentSchema}。");

        if (!root.TryGetProperty("points", out JsonElement pointsElement)
            || pointsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"sweep 缺少 points 数组：{path}");
        }

        var points = new List<SweepPoint>();
        foreach (JsonElement element in pointsElement.EnumerateArray())
            points.Add(new SweepPoint(element.Clone()));

        if (points.Count == 0)
            throw new InvalidOperationException($"sweep points 为空：{path}");

        return new SweepDocument(Path.GetFullPath(path), schema, points);
    }
}

/// <summary>单个 sweep 点：提供指针取数、筛选、分组条件提取。</summary>
public sealed partial record SweepPoint(JsonElement Root)
{
    public bool TryGetNumber(string pointer, string aggregate, out double value, out string? error)
    {
        value = 0;
        error = null;

        if (!JsonPointer.TryResolve(Root, pointer, out JsonElement element))
        {
            error = $"路径 {pointer} 不存在。";
            return false;
        }

        return TryReadNumber(element, aggregate, out value, out error);
    }

    public bool TryGetString(string pointer, out string value, out string? error)
    {
        value = string.Empty;
        error = null;

        if (!JsonPointer.TryResolve(Root, pointer, out JsonElement element))
        {
            error = $"路径 {pointer} 不存在。";
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString() ?? string.Empty;
                return true;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.GetRawText();
                return true;

            default:
                error = $"路径 {pointer} 不是标量。";
                return false;
        }
    }

    public bool Matches(IReadOnlyList<FormulaTargetFilter>? filters, out string? error)
    {
        error = null;
        if (filters is null)
            return true;

        foreach (FormulaTargetFilter filter in filters)
        {
            if (!TryGetString(filter.Path, out string actual, out _))
                return false;

            if (!string.Equals(actual, filter.EqualsValue, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public bool TryGetGroupValues(
        IReadOnlyList<string>? groupBy,
        out SortedDictionary<string, string> conditions,
        out string? error)
    {
        conditions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        error = null;

        if (groupBy is null)
            return true;

        foreach (string pointer in groupBy)
        {
            if (!TryGetString(pointer, out string value, out string? reason))
            {
                error = $"分组路径 {pointer} 无法解析：{reason}";
                return false;
            }

            string key = JsonPointer.LastToken(pointer);
            if (conditions.ContainsKey(key))
                key = pointer;

            conditions[key] = value;
        }

        return true;
    }
}
