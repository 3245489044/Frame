using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Node.Placement;

namespace Share.NodeIo;

/// <summary>载荷校验结果（失败给具体原因，禁压成 empty）。</summary>
public sealed record PayloadValidation(bool Ok, string? Reason)
{
    public static PayloadValidation Pass { get; } = new(true, null);

    public static PayloadValidation Fail(string reason) => new(false, reason);
}

/// <summary>
/// 载荷契约校验（**写侧前置**）：非 Stream 形态载荷为不透明文档（只要求非空）；
/// Stream 形态校验分区键一致 + 必填根字段 + 轮/帧形状（读侧 <see cref="NodePayloadReader"/> 共用同一解析）。
/// </summary>
public static class NodePayloadValidator
{
    public static PayloadValidation Validate(
        NodePayloadSchema schema,
        PlacementForm form,
        string? payloadJson,
        string keyContext)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return PayloadValidation.Fail(NodeDocumentReasons.PayloadMissing);
        }

        if (form != PlacementForm.Stream)
        {
            return PayloadValidation.Pass;
        }

        return TryParseStream(schema, payloadJson, keyContext, out _, out var reason)
            ? PayloadValidation.Pass
            : PayloadValidation.Fail(reason);
    }

    /// <summary>Stream 载荷解析（校验 + 结构产出单源；<paramref name="root"/> 为已校验对象）。</summary>
    internal static bool TryParseStream(
        NodePayloadSchema schema,
        string payloadJson,
        string partition,
        out JsonObject root,
        out string reason)
    {
        root = null!;
        reason = string.Empty;

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(payloadJson);
        }
        catch (JsonException ex)
        {
            reason = NodeDocumentReasons.PayloadNotObject + ":" + ex.Message;
            return false;
        }

        if (parsed is not JsonObject obj)
        {
            reason = NodeDocumentReasons.PayloadNotObject;
            return false;
        }

        root = obj;

        var declaredPartition = obj[schema.PartitionField] is JsonValue value &&
                                value.TryGetValue<string>(out var text)
            ? text
            : null;
        if (!string.Equals(declaredPartition, partition, StringComparison.Ordinal))
        {
            reason = NodeDocumentReasons.PayloadPartitionMismatch +
                     ":" + (declaredPartition ?? "(missing)") + "!=" + partition;
            return false;
        }

        foreach (var field in schema.RequiredOrEmpty)
        {
            if (obj[field] is null)
            {
                reason = NodeDocumentReasons.PayloadRequiredMissing + ":" + field;
                return false;
            }
        }

        if (schema.RoundsField is { Length: > 0 } roundsField)
        {
            if (obj[roundsField] is not JsonArray rounds)
            {
                reason = NodeDocumentReasons.PayloadRoundsMissing;
                return false;
            }

            if (schema.FramesField is not { Length: > 0 } framesField)
            {
                return true;
            }

            foreach (var roundNode in rounds)
            {
                if (roundNode is not JsonObject round)
                {
                    reason = NodeDocumentReasons.PayloadFrameInvalid + ":round-not-object";
                    return false;
                }

                if (round[framesField] is not JsonArray frames)
                {
                    reason = NodeDocumentReasons.PayloadFrameInvalid + ":frames-missing";
                    return false;
                }

                foreach (var frame in frames)
                {
                    if (frame is not JsonObject)
                    {
                        reason = NodeDocumentReasons.PayloadFrameInvalid + ":frame-not-object";
                        return false;
                    }
                }
            }
        }
        else if (schema.FramesField is { Length: > 0 } flatFrames &&
                 obj[flatFrames] is not null &&
                 obj[flatFrames] is not JsonArray)
        {
            reason = NodeDocumentReasons.PayloadFrameInvalid + ":frames-not-array";
            return false;
        }

        return true;
    }
}
