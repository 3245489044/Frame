
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Utilities;

namespace Plate.JsonMethod.Diagnostics
{
    public static class JsonErrorDiagnostics
    {
        public static string Enrich(JsonException ex, string json, Type targetType)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"JSON 反序列化失败: 目标类型={targetType.FullName}");

            var path = ex.Path ?? "(unknown)";
            var lineNumber = ex.LineNumber.HasValue ? ex.LineNumber.Value.ToString() : "(unknown)";
            var position = ex.BytePositionInLine.HasValue ? ex.BytePositionInLine.Value.ToString() : "(unknown)";
            sb.AppendLine($"  路径: {path}");
            sb.AppendLine($"  行号: {lineNumber}, 列位置: {position}");

            if (!string.IsNullOrEmpty(path) && path != "(unknown)")
            {
                try
                {
                    var actualValue = ExtractValueAtPath(json, path);
                    if (actualValue != null) sb.AppendLine($"  实际 JSON 值: {actualValue}");
                }
                catch { }
            }

            // 语法错误（JsonException.Path 为空）时路径不可知 → 不输出「期望类型」，避免
            // “JsonNode.(unknown) (属性不存在)” 之类的误导性结论
            if (!string.IsNullOrEmpty(ex.Path))
            {
                var typeInfo = DescribeExpectedType(targetType, ex.Path!);
                if (typeInfo != null) sb.AppendLine($"  期望类型: {typeInfo}");
            }

            sb.AppendLine($"  原始错误: {ex.Message}");
            return sb.ToString();
        }

        private static string? ExtractValueAtPath(string json, string path)
        {
            try
            {
                var node = JsonNode.Parse(json);
                if (node == null) return null;
                var resolved = JsonPathHelper.ResolveJsonPath(node, path);
                return resolved?.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            }
            catch { return null; }
        }

        private static bool IsGenericCollectionType(Type type, out Type? elementType)
        {
            elementType = null;
            if (!type.IsGenericType) return false;
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
            return false;
        }

        private static Type? TryGetCollectionElementType(Type type)
        {
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return type.GetGenericArguments().FirstOrDefault();
            return null;
        }

        private static string? DescribeExpectedType(Type targetType, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                var cleanPath = path.StartsWith("$.") ? path[2..] : path;
                var segments = cleanPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
                Type currentType = targetType;
                foreach (var segment in segments)
                {
                    var propName = segment;
                    var bracketIdx = segment.IndexOf('[');
                    if (bracketIdx >= 0) propName = segment[..bracketIdx];
                    if (string.IsNullOrEmpty(propName)) continue;

                    if (bracketIdx >= 0)
                    {
                        if (IsGenericCollectionType(currentType, out var elementType))
                        {
                            currentType = elementType!;
                            continue;
                        }
                        if (currentType.IsArray)
                        {
                            currentType = currentType.GetElementType()!;
                            continue;
                        }
                    }

                    var prop = currentType.GetProperty(propName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null)
                    {
                        if (IsGenericCollectionType(currentType, out var fallbackElementType))
                        {
                            currentType = fallbackElementType!;
                            continue;
                        }
                        return $"{currentType.Name}.{propName} (属性不存在)";
                    }
                    currentType = prop.PropertyType;

                    var collectionElementType = TryGetCollectionElementType(currentType);
                    if (collectionElementType != null) currentType = collectionElementType;
                }
                return FormatTypeDescription(currentType);
            }
            catch { return null; }
        }

        private static string FormatTypeDescription(Type type)
        {
            var shortName = GetShortTypeName(type);
            if (shortName != type.Name) return shortName;
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite || p.GetIndexParameters().Length == 0)
                .Select(p => $"{p.Name}: {GetShortTypeName(p.PropertyType)}");
            return $"{type.Name} {{ {string.Join(", ", props)} }}";
        }

        private static string GetShortTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(decimal)) return "decimal";
            if (type.IsGenericType)
            {
                var name = type.Name.Split('`')[0];
                var args = string.Join(", ", type.GetGenericArguments().Select(GetShortTypeName));
                return $"{name}<{args}>";
            }
            return type.Name;
        }
    }
}
