using System.Text.Json;

namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// JSON 契约文本的强类型读取骨架与容错原语：注释 / 尾随逗号容忍；未知键 **fail-fast**
/// （非声明字段不得静默进入契约面）；缺失或类型不符 → null / 空。
/// <para>
/// 辨异（迁移期不得互相替换）：<see cref="Strings"/> 接受裸字符串并包装为单元素数组，
/// <see cref="StrList"/> 不接受 —— 两组原语服务不同读取入口，语义逐字保留。
/// </para>
/// </summary>
public static class JsonLedgerReader
{
    /// <summary>解析选项（注释跳过 + 允许尾随逗号）：消费侧自持解析须与之一致。</summary>
    public static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>解析 → 交给读者 → 释放文档（根元素先 Clone，脱离文档生命周期）。</summary>
    public static T ParseRoot<T>(string json, Func<JsonElement, T> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(body);

        using var document = JsonDocument.Parse(json, ParseOptions);
        return body(document.RootElement.Clone());
    }

    /// <summary>取对象型段（缺失或非对象 → false）。</summary>
    public static bool TrySection(JsonElement root, string name, out JsonElement section)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            section = value;
            return true;
        }

        section = default;
        return false;
    }

    /// <summary>未知键 fail-fast（首个未声明键即抛框架异常）。</summary>
    public static void EnsureKnownKeys(string context, JsonElement obj, IReadOnlyList<string> known)
        => WalkKnownKeys(context, obj, known, throwOnUnknown: true, violations: null);

    /// <summary>未知键一次报全（全部未声明键写入 <paramref name="violations"/>，不抛）。</summary>
    public static void CollectUnknownKeys(string context, JsonElement obj, IReadOnlyList<string> known, ICollection<string> violations)
        => WalkKnownKeys(context, obj, known, throwOnUnknown: false, violations: violations);

    /// <summary>未知键诊断文案（单一构造点：抛与收集共用同一文案；逐字沿用迁移前文本）。</summary>
    public static string UnknownKeyMessage(string context, string name)
        => $"未知键 '{context}.{name}'（fail-fast：非标准字段不得静默进入标准面，元数据请用 $comment）";

    private static void WalkKnownKeys(
        string context, JsonElement obj, IReadOnlyList<string> known, bool throwOnUnknown, ICollection<string>? violations)
    {
        if (obj.ValueKind != JsonValueKind.Object) return;

        foreach (var property in obj.EnumerateObject())
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                var message = UnknownKeyMessage(context, property.Name);
                if (throwOnUnknown) throw new DeclarationViolationException(message);
                violations?.Add(message);
            }
    }

    /// <summary>点链取值（如 <c>out.fields</c>）→ 字符串数组（缺失 → 空）。</summary>
    public static IReadOnlyList<string> PathStrings(JsonElement entry, string dottedPath)
    {
        var current = entry;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next)) return [];
            current = next;
        }

        return current.ValueKind == JsonValueKind.Array
            ? current.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray()
            : [];
    }

    /// <summary>字符串数组读取（数组取字符串元素；裸字符串包为单元素；其余 → 空）。</summary>
    public static IReadOnlyList<string> Strings(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value)) return [];

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray(),
            JsonValueKind.String => [value.GetString()!],
            _ => [],
        };
    }

    /// <summary>字符串读取（缺失或非字符串 → null）。</summary>
    public static string? Text(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>数值读取（缺失或非数值 → null）。</summary>
    public static double? Number(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    /// <summary>字符串数组字段读取（缺失或非数组 → null；**不接受裸字符串**）。</summary>
    public static IReadOnlyList<string>? StrList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    /// <summary>元素数组字段读取（缺失或非数组 → null；元素 Clone 脱离文档生命周期）。</summary>
    public static IReadOnlyList<JsonElement>? Elements(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    /// <summary>整数读取（缺失或非整数 → null）。</summary>
    public static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    /// <summary>字符串读取（缺失或非字符串 → null）。</summary>
    public static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>布尔读取（缺失或非布尔 → null）。</summary>
    public static bool? Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}
