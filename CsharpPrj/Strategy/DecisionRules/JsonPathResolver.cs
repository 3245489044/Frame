using System.Globalization;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Utilities;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// JSONPath 取值解析器（输入面泛化核心）。复用 <see cref="JsonPathHelper.ResolveJsonPath"/>
    /// 对 JSON 根解析，不重写路径引擎（SingleImplementation）。提供标量文本/节点/数值比较等读取便捷，
    /// 供匹配器（JsonPath / arrayContains / numericCompare）与模板渲染共用。
    /// </summary>
    public static class JsonPathResolver
    {
        /// <summary>取 path 指向的节点；不存在/非法路径返回 null。</summary>
        public static JsonNode? Resolve(JsonNode? root, string? jsonPath)
        {
            if (root is null || string.IsNullOrWhiteSpace(jsonPath)) return null;
            try
            {
                var path = Normalize(jsonPath);
                return string.IsNullOrEmpty(path)
                    ? root
                    : JsonPathHelper.ResolveJsonPath(root, path);
            }
            catch
            {
                return null; // 非法路径/类型失配：安全返回，不炸求值。
            }
        }

        /// <summary>取 path 指向的标量文本：标量取底层字符串/原值文本；对象/数组取紧凑 JSON。</summary>
        public static string? ResolveText(JsonNode? root, string? jsonPath)
        {
            var node = Resolve(root, jsonPath);
            if (node is null) return null;
            return NodeToText(node);
        }

        /// <summary>把节点转为可比较文本：JsonValue 取底层标量文本，复合取紧凑 JSON。</summary>
        public static string NodeToText(JsonNode node)
        {
            if (node is JsonValue v)
            {
                if (v.TryGetValue<string>(out var s)) return s;
                if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
                if (v.TryGetValue<double>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
                return v.ToJsonString();
            }
            return node.ToJsonString();
        }

        /// <summary>以 JSONPath 定位到的数组节点；不存在/非数组返回 null。</summary>
        public static JsonArray? ResolveArray(JsonNode? root, string? jsonPath)
            => Resolve(root, jsonPath) as JsonArray;

        /// <summary>以 JSONPath 定位到的数值节点；不存在/非数值返回 null。</summary>
        public static double? ResolveNumber(JsonNode? root, string? jsonPath)
        {
            var node = Resolve(root, jsonPath);
            if (node is not JsonValue v) return null;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
            return null;
        }

        /// <summary>把 "$" / "$.a.b" / "a.b" 归一为 JsonPathHelper 可读形态（$.a.b）。</summary>
        public static string Normalize(string? jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath)) return string.Empty;
            var p = jsonPath.Trim();
            if (p == "$") return string.Empty;
            return p.StartsWith("$", StringComparison.Ordinal) ? p : "$." + p;
        }
    }
}
