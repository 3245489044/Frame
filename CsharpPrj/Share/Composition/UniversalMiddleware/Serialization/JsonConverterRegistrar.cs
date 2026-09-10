
using System.Text.Json;
using System.Text.Json.Serialization;

using JP = Plate.JsonMethod;

namespace UniversalMiddleware.Serialization;

public static class JsonConverterRegistrar
{
    private static bool _registered;

    private static readonly object _lock = new();

    public static Action<string>? Logger { get; set; }

    public static void RegisterAll()
    {
        lock (_lock)
        {
            if (_registered) return;

            Logger?.Invoke("[JsonConverterRegistrar] 开始注册所有 Converter...");


            var options = JP.Serialization.JsonSerializerFactory.Contexts.Default;


            _registered = true;
            Logger?.Invoke("[JsonConverterRegistrar] 所有 Converter 注册完成");
        }
    }

    public static List<string> Validate()
    {
        var errors = new List<string>();

        var options = JP.Serialization.JsonSerializerFactory.Contexts.Default;
        var registeredTypes = options.Converters
            .Select(c => c.GetType())
            .ToHashSet();

        var required = new[]
        {
            typeof(JsonStringEnumConverter),
            typeof(JP.Serialization.DynamicObjectConverter),
        };

        foreach (var type in required)
        {
            if (!registeredTypes.Contains(type))
                errors.Add($"缺少必要的 Converter: {type.Name}");
        }

        var duplicates = options.Converters
            .GroupBy(c => c.GetType())
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} (重复 {g.Count()} 次)")
            .ToList();

        if (duplicates.Any())
        {
            errors.AddRange(duplicates.Select(d => $"Converter 重复注册: {d}"));
        }

        return errors;
    }

    internal static void Reset()
    {
        lock (_lock)
        {
            _registered = false;
        }
    }
}
