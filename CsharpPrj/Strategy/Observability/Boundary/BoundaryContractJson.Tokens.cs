using System.Text.Json;
using Share.HierarchySupport;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Boundary;

/// <summary>边界契约 JSON 的基础取值与枚举记号解析（E9 加载器内部件）。</summary>
public static partial class BoundaryContractJson
{
    private static bool TrySection(JsonElement root, string name, out JsonElement section)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out section)
            && section.ValueKind == JsonValueKind.Object)
            return true;
        section = default;
        return false;
    }

    private static JsonElement? Property(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? Str(JsonElement element, string name)
        => Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int? Int(JsonElement element, string name)
        => Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool? Bool(JsonElement element, string name)
        => Property(element, name)?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static IReadOnlyList<string> Strings(JsonElement element, string name)
        => Property(element, name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray()
            : [];

    private static BoundaryKind? ParseKind(string? text) => text switch
    {
        null => null,
        _ when text.Equals("Port", StringComparison.OrdinalIgnoreCase) => BoundaryKind.Port,
        _ when text.Equals("Data", StringComparison.OrdinalIgnoreCase) => BoundaryKind.Data,
        _ when text.Equals("Event", StringComparison.OrdinalIgnoreCase) => BoundaryKind.Event,
        _ => throw new BoundaryContractViolationException("(契约文件)", $"未知边界种类 '{text}'"),
    };

    private static BoundaryDirection ParseDirection(string? text, BoundaryKind kind) => text switch
    {
        null => kind == BoundaryKind.Port ? BoundaryDirection.Both : BoundaryDirection.In,
        _ when text.Equals("In", StringComparison.OrdinalIgnoreCase) => BoundaryDirection.In,
        _ when text.Equals("Out", StringComparison.OrdinalIgnoreCase) => BoundaryDirection.Out,
        _ when text.Equals("Both", StringComparison.OrdinalIgnoreCase) => BoundaryDirection.Both,
        _ => throw new BoundaryContractViolationException("(契约文件)", $"未知边界方向 '{text}'"),
    };

    private static DataScope ParseScope(string? text) => text switch
    {
        null => DataScope.Round,
        _ when text.Equals("Root", StringComparison.OrdinalIgnoreCase) => DataScope.Root,
        _ when text.Equals("Round", StringComparison.OrdinalIgnoreCase) => DataScope.Round,
        _ when text.Equals("Lease", StringComparison.OrdinalIgnoreCase) => DataScope.Lease,
        _ when text.Equals("Session", StringComparison.OrdinalIgnoreCase) => DataScope.Session,
        _ when text.Equals("Volatile", StringComparison.OrdinalIgnoreCase) => DataScope.Volatile,
        _ => throw new BoundaryContractViolationException("(契约文件)", $"未知作用域 '{text}'"),
    };
}
