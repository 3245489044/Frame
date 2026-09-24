using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modules.Node;

/// <summary>
/// 框架 JSON 边界单源（A-3/A-5 收敛）：解析宽容度、紧凑输出、内容摘要与
/// <see cref="JsonNode"/> → 文本投影只在本类声明一处，读写两侧共用（禁各件自写一套）。
/// 语义与既有散落实现逐项一致：尾逗号/注释跳过、紧凑输出、SHA256 大写 hex。
/// </summary>
internal static class NodeJson
{
    /// <summary>解析宽容度单源（尾逗号 / 注释跳过）：读写路径必须一致，禁逐处各配一份。</summary>
    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>紧凑输出单源（落库/端口值一致，禁装饰性空白）。</summary>
    internal static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

    /// <summary>内容摘要（N4 幂等 / N5 冲突判据）：SHA256 → 大写 hex（64 字符）。</summary>
    internal static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>宽松解析：失败 ⇒ false 并回传原因（调用方决定诊断/兜底，禁 JsonException 穿透）。</summary>
    internal static bool TryParse(string? json, out JsonNode? node, out string? error)
    {
        node = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(json, documentOptions: DocumentOptions);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>值投影单源（端口值 / 读投影字段共用）：字符串直取，其余紧凑 JSON；∅ ⇒ 空串。</summary>
    internal static string ValueToText(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue<string>(out var text) => text ?? string.Empty,
        _ => node.ToJsonString(WriteOptions),
    };

    /// <summary>声明面字符串读取：类型不符 ⇒ false（不抛 InvalidOperationException 穿透）。</summary>
    internal static bool TryReadString(JsonNode? node, out string? text)
    {
        text = null;

        if (node is null)
        {
            return false;
        }

        try
        {
            text = node.GetValue<string>();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>声明面整数读取：类型不符 ⇒ false（不抛 InvalidOperationException 穿透）。</summary>
    internal static bool TryReadInt(JsonNode? node, out int value)
    {
        value = 0;

        if (node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<int>();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
