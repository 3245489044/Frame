using System.Text;
using System.Text.Json;

namespace Plate.JsonMethod.Utilities
{
    /// <summary>
    /// 原文层重复键扫描：JSON 对象内的同名键在 <see cref="JsonDocument"/> 层被**静默合并**，
    /// 故重复键判据只能在原始文本层扫描（逐 token 走 <see cref="Utf8JsonReader"/>，栈式跟踪对象深度）。
    /// </summary>
    public static class JsonDuplicateKeyScanner
    {
        /// <summary>扫描 JSON 文本中的重复键（同一对象内同名；Ordinal）。</summary>
        public static IReadOnlyList<string> Scan(string json)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);

            var duplicates = new List<string>();
            var stack = new Stack<HashSet<string>>();
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        stack.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        if (stack.Count > 0) stack.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString() ?? string.Empty;
                        if (stack.Count > 0 && !stack.Peek().Add(name)) duplicates.Add(name);
                        break;
                }
            }

            return duplicates;
        }
    }
}
