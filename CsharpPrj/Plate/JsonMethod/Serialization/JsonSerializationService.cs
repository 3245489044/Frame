
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Diagnostics;
using Plate.JsonMethod.Utilities;

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
        /// 通用模板：供「解析请求体 → 为空/解析失败 诊断」类宿主复用，消除各调用方重复 try/catch 头部。
        /// <para>严格解析失败时做一次宽松兜底（未转义反斜杠 / 尾随噪声，机制见 <see cref="JsonEscapeRepair"/>）：
        /// 两种改动面分别只是「字符串字面量内反斜杠加倍」与「根值闭合后截断」，不会凭空构造结构；
        /// 且以**完整重解析成功**为唯一采纳门，修复态不缓存、不落盘，失败即回原始诊断。
        /// 落库数据由 <c>JsonSerializer</c> 写出（转义始终合法），故内部数据读取路径实际不会触发该兜底。</para></summary>
        public bool TryDeserialize<T>(string json, JsonContext context, out T? result, out string? error)
            => TryDeserialize<T>(json, context, out result, out error, out _);

        /// <summary>同 <see cref="TryDeserialize{T}(string, JsonContext, out T?, out string?)"/>，另回报宽松修复命中明细（供调用方可见化，修复不得静默）。</summary>
        public bool TryDeserialize<T>(string json, JsonContext context, out T? result, out string? error, out JsonRepairReport report)
        {
            result = default;
            error = null;
            report = default;
            try
            {
                result = Deserialize<T>(json, context);
                return true;
            }
            catch (JsonException strictFailure)
            {
                if (JsonEscapeRepair.TryRepair(json, out var repaired, out var repair))
                {
                    try
                    {
                        result = Deserialize<T>(repaired, context);
                        report = repair;
                        Logger?.Invoke($"[JsonSerializationService] 宽松修复后解析成功: 反斜杠 {repair.EscapeHits} 处 / 尾随噪声 {repair.TrimmedChars} 字符");
                        return true;
                    }
                    catch (Exception)
                    {
                        // 修复后仍非法：回原始诊断，绝不使用修复中间态。
                    }
                }

                error = strictFailure.Message;
                return false;
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
