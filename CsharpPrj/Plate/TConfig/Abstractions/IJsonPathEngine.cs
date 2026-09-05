
using System.Text.Json.Nodes;

namespace TConfig.Abstractions
{
    public interface IJsonPathEngine
    {
        JsonNode? Navigate(JsonNode root, string path);
        bool TryNavigate(JsonNode root, string path, out JsonNode? result);
        bool TryGetValue<T>(JsonNode root, string path, out T? value);
        JsonNode? SetValue(JsonNode root, string path, JsonNode? value);
        Task<JsonNode?> QueryAsync(JsonNode root, string pathExpression);
        IEnumerable<string> GetPaths(JsonNode root, string? filterPattern = null);
    }
}
