
using System.Text.Json;

namespace Modules.Parsing.Parsing
{
    public static class JsonPathHelper
    {
        public static JsonElement? NavigateTo(JsonElement root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var segments = path.Split('.');
            JsonElement current = root;

            foreach (var segment in segments)
            {
                if (!current.TryGetProperty(segment, out current))
                    return null;
            }

            return current;
        }

        public static bool TryGetValue<T>(JsonElement root, string path, out T? value)
        {
            value = default;
            var element = NavigateTo(root, path);
            if (element == null)
                return false;

            try
            {
                var json = element.Value.GetRawText();
                value = System.Text.Json.JsonSerializer.Deserialize<T>(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static PathSegment[] ParsePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Array.Empty<PathSegment>();

            var segments = new List<PathSegment>();
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part == "*")
                {
                    segments.Add(new WildcardSegment());
                }
                else if (part.EndsWith("]"))
                {
                    var bracketIdx = part.IndexOf('[');
                    if (bracketIdx >= 0)
                    {
                        var propName = part[..bracketIdx];
                        var indexStr = part[(bracketIdx + 1)..^1];
                        if (!string.IsNullOrEmpty(propName))
                            segments.Add(new PropertySegment(propName));
                        if (int.TryParse(indexStr, out var idx))
                            segments.Add(new IndexSegment(idx));
                    }
                }
                else
                {
                    segments.Add(new PropertySegment(part));
                }
            }
            return segments.ToArray();
        }

        public static System.Text.Json.Nodes.JsonNode? NavigateWithSegments(
            System.Text.Json.Nodes.JsonNode root, PathSegment[] segments)
        {
            var node = root;
            foreach (var seg in segments)
            {
                if (node == null) return null;
                node = seg switch
                {
                    PropertySegment p => (node as System.Text.Json.Nodes.JsonObject)
                        ?.TryGetPropertyValue(p.Name, out var child) == true ? child : null,
                    IndexSegment i => (node as System.Text.Json.Nodes.JsonArray) is
                        { Count: > 0 } arr && i.Index < arr.Count ? arr[i.Index] : null,
                    WildcardSegment => HandleWildcard(node),
                    _ => null
                };
            }
            return node;
        }

        private static System.Text.Json.Nodes.JsonNode? HandleWildcard(System.Text.Json.Nodes.JsonNode node)
        {
            if (node is System.Text.Json.Nodes.JsonArray arr && arr.Count > 0)
                return arr[0];
            return node;
        }

        public abstract record PathSegment();

        public record PropertySegment(string Name) : PathSegment();

        public record IndexSegment(int Index) : PathSegment();

        public record WildcardSegment() : PathSegment();
    }
}
