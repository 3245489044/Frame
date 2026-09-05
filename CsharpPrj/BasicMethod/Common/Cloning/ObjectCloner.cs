
#nullable enable

using Force.DeepCloner;

namespace Modules.Common.Cloning
{
    public interface IObjectCloner<T> where T : class
    {
        T Clone(T source);
    }

    public class DeepClonerAdapter<T> : IObjectCloner<T> where T : class
    {
        public T Clone(T source)
        {
            return source.DeepClone();
        }
    }

    public class JsonCloner<T> : IObjectCloner<T> where T : class
    {
        private readonly System.Text.Json.JsonSerializerOptions _options;

        public JsonCloner(System.Text.Json.JsonSerializerOptions? options = null)
        {
            _options = options ?? new System.Text.Json.JsonSerializerOptions
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            };
        }

        public T Clone(T source)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(source, _options);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, _options)
                ?? throw new InvalidOperationException($"Failed to clone object of type {typeof(T).Name}");
        }
    }
}
