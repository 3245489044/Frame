#nullable enable

using System;
using System.Globalization;
using System.Text.Json;

namespace UiBackend.Html;

/// <summary>
/// DOM click 回执（页面侧结构事实）。带 opId 令牌：只有「本次点击」写入的回执才算送达，
/// 消除全局单槽被上一次/其它绑定覆盖导致的「末次结果冒充本次」。
/// </summary>
public sealed record ClickAck(
    string? OpId,
    string? Name,
    string? Tag,
    bool Trusted,
    long At,
    string? PathTag,
    bool InDocument,
    int Cx = -1,
    int Cy = -1,
    int Attempt = 0,
    double Left = double.NaN,
    double Top = double.NaN,
    double Width = double.NaN,
    double Height = double.NaN)
{
    /// <summary>令牌匹配：缺令牌的回执一律不认（防旧拍残留）。</summary>
    public bool Matches(string opId)
        => !string.IsNullOrEmpty(OpId) && string.Equals(OpId, opId, StringComparison.Ordinal);

    /// <summary>本次尝试号匹配；旧版页面未带 attempt（=0）时按兼容放行。</summary>
    public bool MatchesAttempt(int attempt) => Attempt == 0 || Attempt == attempt;

    /// <summary>回执矩形是否与探测矩形一致（中心点 + 尺寸，容差 2 CSS px）；缺矩形视为不可判定（放行，仅取证）。</summary>
    public bool RectMatches(double centerX, double centerY, double width, double height, double tolerance = 2.0)
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return true;
        return Math.Abs(Left + Width / 2 - centerX) <= tolerance
            && Math.Abs(Top + Height / 2 - centerY) <= tolerance
            && Math.Abs(Width - width) <= tolerance
            && Math.Abs(Height - height) <= tolerance;
    }

    public string ToEvidence()
        => $"ackName={Name ?? "-"} ackTag={Tag ?? "-"} ackTrusted={Trusted} ackAt={At} ackAttempt={Attempt} ackClient=({Cx},{Cy}) "
           + $"ackRect=({Left.ToString("F1", CultureInfo.InvariantCulture)},{Top.ToString("F1", CultureInfo.InvariantCulture)},"
           + $"{Width.ToString("F1", CultureInfo.InvariantCulture)}x{Height.ToString("F1", CultureInfo.InvariantCulture)}) "
           + $"ackPath={PathTag ?? "-"} ackInDocument={InDocument}";

    /// <summary>解析回执 JSON；"null"/空/非法/**缺 opId** ⇒ false（<c>{}</c> 等空对象不得冒充回执）。</summary>
    public static bool TryParse(string? json, out ClickAck? ack)
    {
        ack = null;
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var opId = GetString(root, "opId");
            if (string.IsNullOrEmpty(opId)) return false; // 无令牌 = 非本次回执（Promise 对象/旧拍/他源）
            ack = new ClickAck(
                opId,
                GetString(root, "name"),
                GetString(root, "tag"),
                root.TryGetProperty("trusted", out var trusted) && trusted.ValueKind == JsonValueKind.True,
                root.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.Number ? at.GetInt64() : 0,
                GetString(root, "pathTag"),
                root.TryGetProperty("inDocument", out var inDoc) && inDoc.ValueKind == JsonValueKind.True,
                root.TryGetProperty("cx", out var cx) && cx.ValueKind == JsonValueKind.Number ? cx.GetInt32() : -1,
                root.TryGetProperty("cy", out var cy) && cy.ValueKind == JsonValueKind.Number ? cy.GetInt32() : -1,
                root.TryGetProperty("attempt", out var attempt) && attempt.ValueKind == JsonValueKind.Number ? attempt.GetInt32() : 0,
                GetNestedDouble(root, "rect", "left"),
                GetNestedDouble(root, "rect", "top"),
                GetNestedDouble(root, "rect", "width"),
                GetNestedDouble(root, "rect", "height"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string AtText() => At.ToString(CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double GetNestedDouble(JsonElement element, string objectName, string field)
    {
        if (!element.TryGetProperty(objectName, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return double.NaN;
        return nested.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : double.NaN;
    }
}
