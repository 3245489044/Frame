#nullable enable
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentValidation;
using UniversalMiddleware.Abstractions;

using JP = Plate.JsonMethod;

namespace UniversalMiddleware.Serialization
{
    public class UnifiedJsonSerializer : IJsonSerializer
    {
        private static readonly JP.Serialization.JsonSerializationService _inner =
            JP.Serialization.JsonSerializationService.Instance;

        public static JP.Utilities.JsonPreprocessor Preprocessor { get; } = new();

        public static Action<string>? Logger
        {
            get => _inner.Logger;
            set => _inner.Logger = value;
        }


        public static JsonSerializerOptions DefaultOptions =>
            JP.Serialization.JsonSerializerFactory.Contexts.Default;

        public static JsonSerializerOptions IndentedOptions =>
            JP.Serialization.JsonSerializerFactory.Contexts.PrettyPrint;


        public string Serialize<T>(T obj, JsonSerializerOptions? options = null)
            => options != null
                ? _inner.Serialize(obj, options)
                : _inner.Serialize(obj);

        public T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
            => options != null
                ? _inner.Deserialize<T>(json, options)
                : _inner.Deserialize<T>(json);

        public string SafeSerialize<T>(T obj, JsonSerializerOptions? options = null)
            => options != null
                ? _inner.SafeSerialize(obj, options)
                : _inner.SafeSerialize(obj);

        public T? DeserializeWithValidation<T>(string json, Action<JsonValidationResult>? onValidate = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;

            var (result, errors) = _inner.DeserializeWithValidation<T>(json);
            if (errors.Count > 0)
            {
                onValidate?.Invoke(JsonValidationResult.Failure(errors.ToArray()));
                return default;
            }

            var modelValidation = ValidateModel(result);
            onValidate?.Invoke(modelValidation);
            return result;
        }

        public async Task SerializeToFileAsync<T>(string path, T obj, JsonSerializerOptions? options = null)
        {
            if (options != null)
                await _inner.SerializeToFileAsync(path, obj, options);
            else
                await _inner.SerializeToFileAsync(path, obj);
        }

        public async Task<T?> DeserializeFromFileAsync<T>(string path, JsonSerializerOptions? options = null)
            => options != null
                ? await _inner.LoadFromFileAsync<T>(path, options)
                : await _inner.LoadFromFileAsync<T>(path);

        public T? LoadFromFile<T>(string path, JsonSerializerOptions? options = null)
            => options != null
                ? _inner.LoadFromFile<T>(path, options)
                : _inner.LoadFromFile<T>(path);

        public void SaveToFile<T>(string path, T obj, JsonSerializerOptions? options = null)
        {
            if (options != null)
                _inner.SaveToFile(path, obj, options);
            else
                _inner.SaveToFile(path, obj);
        }


        public string SerializeCompact<T>(T obj)
            => _inner.Serialize(obj);

        public T DeserializeCompact<T>(string data)
            => _inner.Deserialize<T>(data)!;

        public object DeserializeCompact(string data, Type type)
            => JsonSerializer.Deserialize(data, type, DefaultOptions)!;


        internal static JsonValidationResult ToLocalValidationResult(JP.Validation.JsonValidationResult jpResult)
        {
            if (jpResult.IsValid)
                return JsonValidationResult.Success();
            return JsonValidationResult.Failure(jpResult.Errors);
        }


        private static JsonValidationResult ValidateModel<T>(T? model)
            => ModelValidator.ValidateModelInternal(model);
    }

    internal static class ModelValidator
    {
        private static readonly ConcurrentDictionary<Type, IValidator> _validators = new();

        public static void RegisterValidator<T>(IValidator<T> validator)
        {
            _validators[typeof(T)] = validator;
        }

        public static JsonValidationResult ValidateModelInternal<T>(T? model)
        {
            if (model == null)
                return JsonValidationResult.Failure("Model is null");

            if (_validators.TryGetValue(typeof(T), out var validator))
            {
                var context = new ValidationContext<T>(model);
                var result = validator.Validate(context);
                if (result.IsValid)
                    return JsonValidationResult.Success();
                return JsonValidationResult.Failure(result.Errors.Select(e => e.ErrorMessage).ToArray());
            }

            return LegacyValidate(model);
        }

        private static JsonValidationResult LegacyValidate<T>(T model)
        {
            var errors = new List<string>();
            if (model == null)
            {
                errors.Add("Model is null");
                return new JsonValidationResult(false, errors);
            }
            var type = model.GetType();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = prop.GetValue(model);
                var displayName = prop.GetCustomAttribute<DataMemberAttribute>()?.Name ?? prop.Name;

                if (prop.GetCustomAttribute<RequiredAttribute>() != null)
                {
                    if (value == null || (prop.PropertyType == typeof(string) && string.IsNullOrEmpty(value as string)))
                        errors.Add($"'{displayName}' is required");
                }

                if (prop.GetCustomAttribute<StringLengthAttribute>() is { } lenAttr)
                {
                    if (value is string str && str.Length > lenAttr.MaximumLength)
                        errors.Add($"'{displayName}' exceeds max length of {lenAttr.MaximumLength}");
                }

                if (prop.GetCustomAttribute<RangeAttribute>() is { } rangeAttr)
                {
                    if (value is IComparable comp && (comp.CompareTo(rangeAttr.Minimum) < 0 || comp.CompareTo(rangeAttr.Maximum) > 0))
                        errors.Add($"'{displayName}' must be between {rangeAttr.Minimum} and {rangeAttr.Maximum}");
                }
            }

            return errors.Count == 0
                ? JsonValidationResult.Success()
                : JsonValidationResult.Failure(errors.ToArray());
        }
    }
}

namespace UniversalMiddleware.Validator
{
    public class JsonValidator : IJsonValidator
    {
        private readonly JP.Validation.JsonValidationService _jpValidator = new();

        public JsonValidationResult ValidateSyntax(string json)
        {
            var result = _jpValidator.ValidateSyntax(json);
            return Serialization.UnifiedJsonSerializer.ToLocalValidationResult(result);
        }

        public JsonValidationResult ValidateStructure<T>(string json)
        {
            var syntaxResult = ValidateSyntax(json);
            if (!syntaxResult.IsValid) return syntaxResult;

            try
            {
                var result = JsonSerializer.Deserialize<T>(json, Serialization.UnifiedJsonSerializer.DefaultOptions);
                return result != null
                    ? JsonValidationResult.Success()
                    : JsonValidationResult.Failure($"Deserialization to {typeof(T).Name} produced null");
            }
            catch (JsonException ex)
            {
                return JsonValidationResult.Failure($"Structure validation failed: {ex.Message}");
            }
        }

        public JsonValidationResult ValidateModel<T>(T model)
            => Serialization.ModelValidator.ValidateModelInternal(model);

    
    }
}

namespace UniversalMiddleware.Transformation
{
    public static class JsonMerger
    {
        public static JsonObject Merge(JsonObject baseObj, JsonObject overrideObj, MergeOptions options = MergeOptions.ReplaceArray)
        {
            var arrayMode = options switch
            {
                MergeOptions.ReplaceArray => JP.Utilities.JsonArrayMergeMode.Replace,
                MergeOptions.AppendArray => JP.Utilities.JsonArrayMergeMode.Append,
                _ => JP.Utilities.JsonArrayMergeMode.Replace
            };
            return JP.Utilities.JsonMergeService.Merge(baseObj, overrideObj, arrayMode);
        }

        public static T MergeModels<T>(T baseModel, T overrideModel) where T : class, new()
            => JP.Utilities.JsonMergeService.MergeModels(baseModel, overrideModel);
    }

    public enum MergeOptions
    {
        ReplaceArray,
        AppendArray
    }

    public class ModelTransform<TSource, TTarget> : Core.TransformBase<TSource, TTarget>
    {
        private readonly Func<TSource, TTarget> _transformFunc;

        public override string Name => $"Transform<{typeof(TSource).Name}→{typeof(TTarget).Name}>";

        public ModelTransform(Func<TSource, TTarget> transformFunc)
        {
            _transformFunc = transformFunc;
        }

        public override Task<TTarget> ProcessAsync(TSource input, JsonPipelineContext context)
            => Task.FromResult(_transformFunc(input));
    }

    public static class JsonNodeConverter
    {
        public static JsonNode? FromObject(object obj)
            => JsonSerializer.SerializeToNode(obj, Serialization.UnifiedJsonSerializer.DefaultOptions);

        public static T? ToObject<T>(JsonNode? node)
            => node == null ? default : JsonSerializer.Deserialize<T>(node.ToJsonString(), Serialization.UnifiedJsonSerializer.DefaultOptions);
    }
}

namespace UniversalMiddleware.Navigation
{
    public class JsonPathEngine : IJsonPathEngine
    {
        public JsonNode? Navigate(JsonNode root, string path)
            => JP.Utilities.JsonPathHelper.ResolveJsonPath(root, path);

        public bool TryNavigate(JsonNode root, string path, out JsonNode? result)
        {
            result = JP.Utilities.JsonPathHelper.ResolveJsonPath(root, path);
            return result != null;
        }

        public bool TryGetValue<T>(JsonNode root, string path, out T? value)
        {
            var node = JP.Utilities.JsonPathHelper.ResolveJsonPath(root, path);
            if (node != null)
            {
                try
                {
                    value = node.GetValue<T>();
                    return true;
                }
                catch { }
            }
            value = default;
            return false;
        }

        public JsonNode? SetValue(JsonNode root, string path, JsonNode? value)
            => JP.Utilities.JsonPathHelper.SetJsonPath(root, path, value);

        public Task<JsonNode?> QueryAsync(JsonNode root, string pathExpression)
            => Task.FromResult(JP.Utilities.JsonPathHelper.ResolveJsonPath(root, pathExpression));

        public IEnumerable<string> GetPaths(JsonNode root, string? filterPattern = null)
        {
            var paths = JP.Utilities.JsonPathHelper.GetAllPaths(root);
            return filterPattern != null
                ? paths.Where(p => p.Contains(filterPattern, StringComparison.OrdinalIgnoreCase))
                : paths;
        }
    }
}
