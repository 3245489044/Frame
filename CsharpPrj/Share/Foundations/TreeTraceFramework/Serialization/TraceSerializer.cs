
using System.Text.Json;
using System.Text.Json.Serialization;
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;

namespace Share.TreeTraceFramework.Serialization
{
    public class TraceSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public string Serialize(ITraceTree<TraceNode> tree)
        {
            if (tree?.Root == null)
                return "{}";

            var dto = SerializeNode(tree.Root);
            return JsonSerializer.Serialize(new
            {
                traceId = tree.TraceId,
                createdAt = tree.CreatedAt,
                nodeCount = tree.NodeCount,
                root = dto
            }, JsonOptions);
        }

        public ITraceTree<TraceNode> Deserialize(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var traceId = root.TryGetProperty("traceId", out var tid) ? tid.GetString() : null;
            var tree = new TraceTree(traceId);

            if (root.TryGetProperty("root", out var rootElement))
            {
                DeserializeNode(rootElement, tree, null);
            }

            return tree;
        }

        public async Task SerializeToFileAsync(ITraceTree<TraceNode> tree, string filePath)
        {
            var json = Serialize(tree);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<ITraceTree<TraceNode>> DeserializeFromFileAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            return Deserialize(json);
        }


        private static object SerializeNode(TraceNode node)
        {
            return new
            {
                nodeId = node.NodeId,
                name = node.Name,
                labels = node.Labels.Any() ? node.Labels : null,
                inputs = node.Inputs.Any() ? node.Inputs : null,
                outputs = node.Outputs.Any() ? node.Outputs : null,
                records = node.Records.Any() ? node.Records : null,
                depth = node.Depth,
                createdAt = node.CreatedAt,
                children = node.Children.Any()
                    ? node.Children.Select(SerializeNode).ToList()
                    : null
            };
        }

        private static void DeserializeNode(JsonElement element, TraceTree tree, TraceNode? parent)
        {
            var name = element.GetProperty("name").GetString() ?? "unknown";
            var nodeId = element.TryGetProperty("nodeId", out var nid) ? nid.GetString() : null;

            TraceNode node;
            if (parent == null)
            {
                node = tree.CreateRoot(name);
            }
            else
            {
                node = parent.AddChild(name);
            }

            if (element.TryGetProperty("labels", out var labels))
            {
                foreach (var label in labels.EnumerateArray())
                {
                    var labelStr = label.GetString();
                    if (labelStr != null) node.AddLabel(labelStr);
                }
            }

            if (element.TryGetProperty("inputs", out var inputs))
            {
                foreach (var input in inputs.EnumerateArray())
                {
                    node.AddInput(JsonElementToObject(input));
                }
            }

            if (element.TryGetProperty("outputs", out var outputs))
            {
                foreach (var output in outputs.EnumerateArray())
                {
                    node.AddOutput(JsonElementToObject(output));
                }
            }

            if (element.TryGetProperty("records", out var records))
            {
                foreach (var record in records.EnumerateArray())
                {
                    var recordStr = record.GetString();
                    if (recordStr != null) node.AddRecord(recordStr);
                }
            }

            if (element.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    DeserializeNode(child, tree, node);
                }
            }
        }

        private static object? JsonElementToObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var longVal) ? longVal :
                                        element.TryGetDouble(out var doubleVal) ? doubleVal :
                                        element.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Object => element.GetRawText(),
                JsonValueKind.Array => element.GetRawText(),
                _ => element.GetRawText()
            };
        }
    }
}
