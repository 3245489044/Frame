
using System.Text.Json;

namespace TConfig.Abstractions
{
    public interface IJsonSerializer
    {
        string Serialize<T>(T obj, JsonSerializerOptions? options = null);
        T? Deserialize<T>(string json, JsonSerializerOptions? options = null);
        string SafeSerialize<T>(T obj, JsonSerializerOptions? options = null);
        T? DeserializeWithValidation<T>(string json, Action<JsonValidationResult>? onValidate = null);
        Task SerializeToFileAsync<T>(string path, T obj, JsonSerializerOptions? options = null);
        Task<T?> DeserializeFromFileAsync<T>(string path, JsonSerializerOptions? options = null);
    }
}
