
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Diagnostics;

namespace Plate.JsonMethod.Serialization
{
    public class JsonSerializationService
    {
        private static readonly Lazy<JsonSerializationService> _instance =
            new(() => new JsonSerializationService());

        public static JsonSerializationService Instance => _instance.Value;

        public Func<string, string>? Preprocessor { get; set; }

        public Action<string>? Logger { get; set; }


        public T? Deserialize<T>(string json, JsonContext context = JsonContext.Default)
            => DeserializeInternal<T>(json, GetOptions(context));

        public T? Deserialize<T>(string json, JsonSerializerOptions options)
            => DeserializeInternal<T>(json, options);

        private T? DeserializeInternal<T>(string json, JsonSerializerOptions options)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            var processedJson = Preprocess(json);
            try
            {
                return JsonSerializer.Deserialize<T>(processedJson, options);
            }
            catch (JsonException ex)
            {
                var detail = JsonErrorDiagnostics.Enrich(ex, processedJson, typeof(T));
                Logger?.Invoke($"[JsonSerializationService] 反序列化失败: {detail}");
                throw new JsonException(detail, ex);
            }
        }


        public string Serialize<T>(T obj, JsonContext context = JsonContext.Default)
            => JsonSerializer.Serialize(obj, GetOptions(context));

        public string Serialize<T>(T obj, JsonSerializerOptions options)
            => JsonSerializer.Serialize(obj, options);

        public string SafeSerialize<T>(T obj, JsonContext context = JsonContext.Default)
        {
            try { return Serialize(obj, context); }
            catch { return "{}"; }
        }

        public string SafeSerialize<T>(T obj, JsonSerializerOptions options)
        {
            try { return Serialize(obj, options); }
            catch { return "{}"; }
        }


        public async Task<T?> LoadFromFileAsync<T>(string path, JsonContext context = JsonContext.Default)
            => await LoadFromFileInternalAsync<T>(path, GetOptions(context));

        public async Task<T?> LoadFromFileAsync<T>(string path, JsonSerializerOptions options)
            => await LoadFromFileInternalAsync<T>(path, options);

        private async Task<T?> LoadFromFileInternalAsync<T>(string path, JsonSerializerOptions options)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("JSON file not found", path);
            var json = await File.ReadAllTextAsync(path);
            return DeserializeInternal<T>(json, options);
        }

        public async Task SerializeToFileAsync<T>(string path, T obj, JsonContext context = JsonContext.Default)
        {
            EnsureDirectoryExists(path);
            var json = Serialize(obj, context);
            await File.WriteAllTextAsync(path, json);
        }

        public async Task SerializeToFileAsync<T>(string path, T obj, JsonSerializerOptions options)
        {
            EnsureDirectoryExists(path);
            var json = Serialize(obj, options);
            await File.WriteAllTextAsync(path, json);
        }


        public T? LoadFromFile<T>(string path, JsonContext context = JsonContext.Default)
            => LoadFromFileInternal<T>(path, GetOptions(context));

        public T? LoadFromFile<T>(string path, JsonSerializerOptions options)
            => LoadFromFileInternal<T>(path, options);

        private T? LoadFromFileInternal<T>(string path, JsonSerializerOptions options)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("JSON file not found", path);
            var json = File.ReadAllText(path);
            return DeserializeInternal<T>(json, options);
        }

        public void SaveToFile<T>(string path, T obj, JsonContext context = JsonContext.Default)
            => SaveToFileInternal(path, obj, GetOptions(context));

        public void SaveToFile<T>(string path, T obj, JsonSerializerOptions options)
            => SaveToFileInternal(path, obj, options);

        private void SaveToFileInternal<T>(string path, T obj, JsonSerializerOptions options)
        {
            EnsureDirectoryExists(path);
            var json = Serialize(obj, options);
            File.WriteAllText(path, json);
        }


        public (T? Result, IReadOnlyList<string> Errors) DeserializeWithValidation<T>(
            string json, JsonContext context = JsonContext.Default)
        {
            var errors = new List<string>();
            T? result;
            try
            {
                result = Deserialize<T>(json, context);
            }
            catch (JsonException ex)
            {
                errors.Add($"反序列化失败: {ex.Message}");
                return (default, errors);
            }
            if (default(T) == null && result == null)
                errors.Add("反序列化结果为 null");
            return (result, errors);
        }

        public T? DeepClone<T>(T obj)
        {
            var json = Serialize(obj);
            return Deserialize<T>(json);
        }


        public JsonNode? ParseJsonNode(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var processed = Preprocess(json);
            try
            {
                return JsonNode.Parse(processed);
            }
            catch (JsonException ex)
            {
                var detail = JsonErrorDiagnostics.Enrich(ex, processed, typeof(JsonNode));
                Logger?.Invoke($"[JsonSerializationService] JsonNode 解析失败: {detail}");
                throw new JsonException(detail, ex);
            }
        }

        /// <summary>安全解析（不抛异常）：合法 → node 返回 true；空/非法 → 返回 false、node 为 null。</summary>
        public bool TryParseNode(string json, out JsonNode? node)
        {
            node = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                node = ParseJsonNode(json);
                return node != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>安全反序列化（不抛异常）：解析成功 → 返回 true、result 为结果（可能为 null）；解析异常 → 返回 false、error 为诊断、result 为 default。
        /// 通用模板：供「解析请求体 → 为空/解析失败 诊断」类宿主复用，消除各调用方重复 try/catch 头部。</summary>
        public bool TryDeserialize<T>(string json, JsonContext context, out T? result, out string? error)
        {
            result = default;
            error = null;
            try
            {
                result = Deserialize<T>(json, context);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public JsonNode? LoadJsonNodeFromFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("JSON file not found", path);
            var json = File.ReadAllText(path);
            return ParseJsonNode(json);
        }

        public async Task<JsonNode?> LoadJsonNodeFromFileAsync(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("JSON file not found", path);
            var json = await File.ReadAllTextAsync(path);
            return ParseJsonNode(json);
        }


        private string Preprocess(string json)
        {
            if (Preprocessor != null)
            {
                try { return Preprocessor(json); }
                catch (Exception ex)
                {
                    Logger?.Invoke($"[JsonSerializationService] 预处理失败: {ex.Message}");
                    return json;
                }
            }
            return json;
        }

        private static JsonSerializerOptions GetOptions(JsonContext context)
            => JsonSerializerFactory.GetOptions(context);

        private static void EnsureDirectoryExists(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
