
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plate.JsonMethod.Utilities
{
    public static class JsonPathHelper
    {
        private static string NormalizePath(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath) || jsonPath == "$") return string.Empty;
            return jsonPath.StartsWith("$.") ? jsonPath[2..] :
                   jsonPath.StartsWith("$") ? jsonPath[1..] : jsonPath;
        }

        private static JsonNode? NavigateSegment(JsonNode? current, string segment)
        {
            if (current == null) return null;
            var bracketIndex = segment.IndexOf('[');
            if (bracketIndex >= 0)
            {
                var name = segment[..bracketIndex];
                var indexStr = segment[bracketIndex..].Trim('[', ']');
                if (!string.IsNullOrEmpty(name)) current = current[name];
                if (current is JsonArray arr && int.TryParse(indexStr, out var index))
                    return index >= 0 && index < arr.Count ? arr[index] : null;
                return null;
            }
            return current[segment];
        }

        private static string[] ParseSegments(string jsonPath)
        {
            var path = NormalizePath(jsonPath);
            return string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split('.');
        }

        public static JsonNode? ResolveJsonPath(JsonNode document, string jsonPath)
        {
            var segments = ParseSegments(jsonPath);
            if (segments.Length == 0) return document;
            JsonNode? current = document;
            foreach (var segment in segments)
            {
                current = NavigateSegment(current, segment);
                if (current == null) return null;
            }
            return current;
        }

        public static string? ExtractContextSnippet(string json, string jsonPath, int lines = 3)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(jsonPath)) return null;
            try
            {
                var document = JsonNode.Parse(json);
                if (document == null) return null;
                var node = ResolveJsonPath(document, jsonPath);
                if (node == null) return null;
                var nodeJson = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                var linesArray = json.Split('\n');
                var nodeLines = nodeJson.Split('\n');
                for (int i = 0; i < linesArray.Length; i++)
                {
                    if (linesArray[i].Trim().StartsWith(nodeLines[0].Trim()))
                    {
                        var startLine = Math.Max(0, i - lines);
                        var endLine = Math.Min(linesArray.Length - 1, i + nodeLines.Length + lines);
                        var sb = new StringBuilder();
                        sb.AppendLine($"  上下文 (行 {startLine + 1}-{endLine + 1}):");
                        for (int j = startLine; j <= endLine; j++)
                        {
                            var marker = (j >= i && j < i + nodeLines.Length) ? "→ " : "  ";
                            sb.AppendLine($"  {marker}{j + 1,4}| {linesArray[j]}");
                        }
                        return sb.ToString();
                    }
                }
                return null;
            }
            catch { return null; }
        }

        public static List<string> ExtractUnexpandedTemplates(string json)
        {
            var templates = new List<string>();
            if (string.IsNullOrEmpty(json)) return templates;
            try
            {
                var doc = JsonNode.Parse(json);
                if (doc != null) ExtractTemplatesRecursive(doc, templates);
            }
            catch { }
            return templates.Distinct().ToList();
        }

        private static void ExtractTemplatesRecursive(JsonNode node, List<string> templates)
        {
            if (node is JsonObject obj)
            {
                if (obj.ContainsKey("template"))
                {
                    var templateName = obj["template"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(templateName)) templates.Add(templateName);
                }
                foreach (var kv in obj) { if (kv.Value != null) ExtractTemplatesRecursive(kv.Value, templates); }
            }
            else if (node is JsonArray arr)
            {
                foreach (var item in arr) { if (item != null) ExtractTemplatesRecursive(item, templates); }
            }
        }

        public static JsonNode? SetJsonPath(JsonNode root, string path, JsonNode? value)
        {
            var segments = ParseSegments(path);
            if (segments.Length == 0) return value;

            JsonNode? current = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                current = NavigateSegment(current, segments[i]);
                if (current == null) return root;
            }

            var lastSegment = segments[^1];
            var lastBracket = lastSegment.IndexOf('[');
            if (lastBracket >= 0)
            {
                var lastName = lastSegment[..lastBracket];
                var lastIndexStr = lastSegment[lastBracket..].Trim('[', ']');
                if (!string.IsNullOrEmpty(lastName))
                {
                    if (current[lastName] is JsonArray arr && int.TryParse(lastIndexStr, out var idx))
                    {
                        if (idx >= 0 && idx < arr.Count)
                            arr[idx] = value;
                    }
                }
                else if (current is JsonArray directArr && int.TryParse(lastIndexStr, out var directIdx))
                {
                    if (directIdx >= 0 && directIdx < directArr.Count)
                        directArr[directIdx] = value;
                }
            }
            else
            {
                current[lastSegment] = value;
            }

            return root;
        }

        /// <summary>
        /// 读取某路径下的"字符串 → 字符串"对象段为字典（仅接受 string 值；不存在/非对象/无字符串成员均安全返回）。
        /// 通用版本：宿主只从 JSON 取一段字符串映射（如配置 templates 段），不做整份结构反序列化，字段失配不连带抛错。
        /// </summary>
        public static IReadOnlyDictionary<string, string>? ReadStringMap(JsonNode document, string jsonPath)
        {
            if (document is null || string.IsNullOrWhiteSpace(jsonPath)) return null;
            var node = ResolveJsonPath(document, jsonPath);
            if (node is not JsonObject obj) return null;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in obj)
            {
                if (kv.Value is JsonValue v && v.TryGetValue<string>(out var s))
                    dict[kv.Key] = s;
            }
            return dict.Count > 0 ? dict : null;
        }

        public static IEnumerable<string> GetAllPaths(JsonNode root)
        {
            var paths = new List<string>();
            WalkPaths(root, "$", paths);
            return paths;
        }

        private static void WalkPaths(JsonNode node, string currentPath, List<string> paths)
        {
            if (node is JsonObject obj)
            {
                paths.Add(currentPath);
                foreach (var kv in obj)
                {
                    if (kv.Value != null)
                        WalkPaths(kv.Value, $"{currentPath}.{kv.Key}", paths);
                }
            }
            else if (node is JsonArray arr)
            {
                paths.Add(currentPath);
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] != null)
                        WalkPaths(arr[i], $"{currentPath}[{i}]", paths);
                }
            }
            else
            {
                paths.Add(currentPath);
            }
        }
    }
}
