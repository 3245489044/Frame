using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Spec;

public sealed record ReferenceRenameResult(string Json, IReadOnlyList<string> AffectedPaths);

/// <summary>
/// 引用字段索引（G11 收编）：引用字段清单（声明）→ 反向引用 → 改名级联。
/// 不定义键模板；引用字段一律来自声明。
/// </summary>
public sealed class NodeReferenceIndex : IReferenceIndexPort
{
    private readonly Dictionary<string, List<string>> _reverse = new(StringComparer.Ordinal);
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeReferenceIndex(IEnumerable<string> referenceFields, INodeDiagnosticsSink? diagnostics = null)
    {
        ReferenceFields = referenceFields.Distinct(StringComparer.Ordinal).ToArray();
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public IReadOnlyList<string> ReferenceFields { get; }

    public IReadOnlyList<string> IndexedKeys => _reverse.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    public bool IsReferenceField(string propertyName) =>
        ReferenceFields.Contains(propertyName, StringComparer.Ordinal);

    /// <summary>装载声明文档并建立反向索引（解析失败的文档产诊断，不静默跳过）。</summary>
    public void Load(IReadOnlyList<SpecDocument> documents)
    {
        _reverse.Clear();

        foreach (var document in documents)
        {
            JsonNode? root;
            if (!NodeJson.TryParse(document.Json, out root, out var documentError))
            {
                _diagnostics.Report(new NodeDiagnostic(
                    "reference.parse-error",
                    NodeDiagnosticSeverity.Error,
                    $"引用索引装载失败: {documentError ?? "空文档"}",
                    document.Name));
                continue;
            }

            if (root is null)
            {
                continue;
            }

            Walk(root, "$", (path, propertyName, value) =>
            {
                if (!IsReferenceField(propertyName) || value is not JsonValue jsonValue ||
                    !jsonValue.TryGetValue<string>(out var referenced) || string.IsNullOrEmpty(referenced))
                {
                    return;
                }

                if (!_reverse.TryGetValue(referenced, out var list))
                {
                    list = new List<string>();
                    _reverse[referenced] = list;
                }

                var location = $"{document.Name}:{path}";
                if (!list.Contains(location, StringComparer.Ordinal))
                {
                    list.Add(location);
                }
            });
        }
    }

    public IReadOnlyList<string> ReverseReferences(string key) =>
        _reverse.TryGetValue(key, out var list) ? list.ToArray() : Array.Empty<string>();

    /// <summary>改名级联：引用字段内的键（含 <c>{node}.{port}</c> 前缀形式）整体重写。</summary>
    public ReferenceRenameResult RenameKey(string oldKey, string newKey, string documentJson)
    {
        var affected = new List<string>();

        JsonNode? root;
        if (!NodeJson.TryParse(documentJson, out root, out var parseError))
        {
            _diagnostics.Report(new NodeDiagnostic(
                "reference.parse-error",
                NodeDiagnosticSeverity.Error,
                $"改名级联失败: {parseError ?? "空文档"}",
                oldKey));
            return new ReferenceRenameResult(documentJson, affected);
        }

        if (root is null)
        {
            return new ReferenceRenameResult(documentJson, affected);
        }

        Walk(root, "$", (path, propertyName, value) =>
        {
            if (!IsReferenceField(propertyName) || value is not JsonValue jsonValue ||
                !jsonValue.TryGetValue<string>(out var referenced))
            {
                return;
            }

            var rewritten = Rewrite(referenced, oldKey, newKey);
            if (string.Equals(rewritten, referenced, StringComparison.Ordinal))
            {
                return;
            }

            switch (value.Parent)
            {
                case JsonObject parentObject:
                    parentObject[propertyName] = JsonValue.Create(rewritten);
                    affected.Add(path);
                    break;

                case JsonArray parentArray:
                    var index = parentArray.IndexOf(value);
                    if (index >= 0)
                    {
                        parentArray[index] = JsonValue.Create(rewritten);
                        affected.Add(path);
                    }

                    break;
            }
        });

        return new ReferenceRenameResult(root.ToJsonString(NodeJson.WriteOptions), affected);
    }

    private static string Rewrite(string referenced, string oldKey, string newKey)
    {
        if (string.Equals(referenced, oldKey, StringComparison.Ordinal))
        {
            return newKey;
        }

        var prefix = oldKey + ".";
        return referenced.StartsWith(prefix, StringComparison.Ordinal)
            ? newKey + referenced[oldKey.Length..]
            : referenced;
    }

    private static void Walk(JsonNode node, string path, Action<string, string, JsonNode?> visit)
    {
        if (node is not JsonObject obj)
        {
            return;
        }

        foreach (var pair in obj.ToArray())
        {
            visit($"{path}.{pair.Key}", pair.Key, pair.Value);

            switch (pair.Value)
            {
                case JsonArray array:
                    for (var index = 0; index < array.Count; index++)
                    {
                        var item = array[index];
                        if (item is null)
                        {
                            continue;
                        }

                        var itemPath = $"{path}.{pair.Key}[{index}]";
                        visit(itemPath, pair.Key, item);
                        Walk(item, itemPath, visit);
                    }

                    break;

                case JsonObject nested:
                    Walk(nested, $"{path}.{pair.Key}", visit);
                    break;
            }
        }
    }
}
