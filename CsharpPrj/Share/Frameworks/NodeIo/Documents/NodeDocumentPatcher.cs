using System.Text.Json.Nodes;

namespace Share.NodeIo;

/// <summary>文档补丁执行器（纯函数；RMW 的读写由端口在写门内完成）。</summary>
public static class NodeDocumentPatcher
{
    public static bool TryApply(string json, IReadOnlyList<NodeDocumentPatch> patches, out string result, out string reason)
    {
        result = json;
        reason = string.Empty;
        if (patches is not { Count: > 0 }) { reason = "patch-empty"; return false; }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            reason = "payload-not-json:" + ex.Message;
            return false;
        }

        if (root is null) { reason = "payload-not-json:empty"; return false; }

        var document = root.DeepClone();
        foreach (var patch in patches)
        {
            if (!TryApplyOne(document, patch, out var next, out reason)) return false;
            document = next;
        }

        result = document.ToJsonString();
        return true;
    }

    private static bool TryApplyOne(JsonNode root, NodeDocumentPatch patch, out JsonNode result, out string reason)
    {
        result = root;
        reason = string.Empty;
        if (!TryPointer(patch.Path, out var segments, out reason)) return false;

        if (segments.Length == 0)
        {
            if (patch.Op != NodePatchOp.Set || patch.ValueJson is null) { reason = "patch-root-op"; return false; }
            var parsed = JsonNode.Parse(patch.ValueJson);
            if (parsed is null) { reason = "patch-value-null"; return false; }
            result = parsed;
            return true;
        }

        var parent = Traverse(root, segments[..^1], create: patch.Op != NodePatchOp.Remove, out reason);
        if (parent is null) return false;

        var leaf = segments[^1];
        if (parent is JsonObject obj) return ApplyObject(obj, leaf, patch, out result, out reason);
        if (parent is JsonArray array) return ApplyArray(array, leaf, patch, out result, out reason);
        reason = "patch-path-not-container";
        return false;
    }

    private static bool ApplyObject(JsonObject target, string leaf, NodeDocumentPatch patch, out JsonNode result, out string reason)
    {
        result = target;
        reason = string.Empty;
        switch (patch.Op)
        {
            case NodePatchOp.Remove:
                target.Remove(leaf);
                return true;
            case NodePatchOp.Merge:
            {
                if (JsonNode.Parse(patch.ValueJson ?? "null") is not JsonObject incoming)
                { reason = "patch-merge-value-not-object"; return false; }
                if (target[leaf] is not JsonObject existing)
                { target[leaf] = incoming.DeepClone(); return true; }
                foreach (var pair in incoming) existing[pair.Key] = pair.Value?.DeepClone();
                return true;
            }
            case NodePatchOp.Append:
            {
                if (target[leaf] is not JsonArray array) { array = new JsonArray(); target[leaf] = array; }
                var value = JsonNode.Parse(patch.ValueJson ?? "null");
                if (value is null) { reason = "patch-value-null"; return false; }
                array.Add(value);
                return true;
            }
            default:
                if (patch.ValueJson is null) { reason = "patch-value-missing"; return false; }
                target[leaf] = JsonNode.Parse(patch.ValueJson);
                return true;
        }
    }

    private static bool ApplyArray(JsonArray target, string leaf, NodeDocumentPatch patch, out JsonNode result, out string reason)
    {
        result = target;
        reason = string.Empty;
        if (!int.TryParse(leaf, out var index) || index < 0) { reason = "patch-array-index-invalid"; return false; }

        switch (patch.Op)
        {
            case NodePatchOp.Remove:
                if (index < target.Count) target.RemoveAt(index);
                return true;
            case NodePatchOp.Append:
            {
                var appended = JsonNode.Parse(patch.ValueJson ?? "null");
                if (appended is null) { reason = "patch-value-null"; return false; }
                target.Add(appended);
                return true;
            }
            case NodePatchOp.Merge:
                reason = "patch-merge-not-object";
                return false;
            default:
            {
                if (patch.ValueJson is null) { reason = "patch-value-missing"; return false; }
                var value = JsonNode.Parse(patch.ValueJson);
                if (index == target.Count) { target.Add(value); return true; }
                if (index > target.Count) { reason = "patch-array-index-invalid"; return false; }
                target[index] = value;
                return true;
            }
        }
    }

    private static JsonNode? Traverse(JsonNode current, string[] segments, bool create, out string reason)
    {
        reason = string.Empty;
        foreach (var segment in segments)
        {
            if (current is JsonObject obj)
            {
                if (obj[segment] is { } child) { current = child; continue; }
                if (!create) { reason = "patch-path-missing"; return null; }
                var created = new JsonObject();
                obj[segment] = created;
                current = created;
                continue;
            }

            if (current is JsonArray array)
            {
                if (!int.TryParse(segment, out var index) || index < 0 || index >= array.Count || array[index] is not { } item)
                { reason = "patch-array-index-invalid"; return null; }
                current = item;
                continue;
            }

            reason = "patch-path-not-container";
            return null;
        }

        return current;
    }

    private static bool TryPointer(string path, out string[] segments, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(path)) { segments = Array.Empty<string>(); return true; }
        if (path[0] != '/') { segments = Array.Empty<string>(); reason = "patch-path-invalid"; return false; }
        segments = path[1..].Split('/').Select(s => s.Replace("~1", "/").Replace("~0", "~")).ToArray();
        return true;
    }
}
