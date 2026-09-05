
using System.Text.Json;
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Core;

public class ControlAdapterRegistryOptions
{
    public IEnumerable<string>? UnifiedJsonPaths { get; set; }

    public string ControlsJsonPath { get; set; } = "ui.views[].controls[]";

    public string ControlNameProperty { get; set; } = "name";

    public string OutputAdapterProperty { get; set; } = "outputAdapter";

    public string InputAdapterProperty { get; set; } = "inputAdapter";

    public string ChildControlsProperty { get; set; } = "controls";
}

public class ControlAdapterRegistry : IControlAdapterRegistry
{
    private readonly Dictionary<string, ControlAdapter> _adapters;
    private readonly ControlAdapterRegistryOptions _options;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ControlAdapterRegistry(ControlAdapterRegistryOptions? options = null)
    {
        _adapters = new Dictionary<string, ControlAdapter>(StringComparer.OrdinalIgnoreCase);
        _options = options ?? new ControlAdapterRegistryOptions();

        if (_options.UnifiedJsonPaths != null)
        {
            foreach (var path in _options.UnifiedJsonPaths)
            {
                LoadFromFile(path);
            }
        }
    }

    public ControlAdapterRegistry(IEnumerable<string> unifiedJsonPaths)
        : this(new ControlAdapterRegistryOptions { UnifiedJsonPaths = unifiedJsonPaths })
    {
    }

    public void LoadFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            var doc = JsonDocument.Parse(json);

            var controlsArrays = ResolveJsonPath(doc.RootElement, _options.ControlsJsonPath);
            foreach (var controlsElement in controlsArrays)
            {
                ParseControls(controlsElement);
            }
        }
        catch
        {
        }
    }

    public object? GetOutputAdapter(object control)
    {
        return GetAdapter(control)?.Output;
    }

    public object? GetInputAdapter(object control)
    {
        return GetAdapter(control)?.Input;
    }

    private ControlAdapter? GetAdapter(object control)
    {
        if (control == null) return null;

        var controlName = GetControlName(control);
        if (string.IsNullOrEmpty(controlName)) return null;

        return _adapters.TryGetValue(controlName, out var adapter) ? adapter : null;
    }

    private static IEnumerable<JsonElement> ResolveJsonPath(JsonElement root, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = new[] { root };

        foreach (var segment in segments)
        {
            if (segment.EndsWith("[]"))
            {
                var propName = segment[..^2];
                current = current
                    .SelectMany(e => e.TryGetProperty(propName, out var arr) && arr.ValueKind == JsonValueKind.Array
                        ? arr.EnumerateArray()
                        : Enumerable.Empty<JsonElement>())
                    .ToArray();
            }
            else
            {
                current = current
                    .Select(e => e.TryGetProperty(segment, out var prop) ? prop : default)
                    .Where(p => p.ValueKind != JsonValueKind.Undefined)
                    .ToArray();
            }

            if (current.Length == 0)
                break;
        }

        return current;
    }

    private void ParseControls(JsonElement controlsElement)
    {
        foreach (var control in controlsElement.EnumerateArray())
        {
            if (!control.TryGetProperty(_options.ControlNameProperty, out var nameElement))
                continue;

            var controlName = nameElement.GetString();
            if (string.IsNullOrEmpty(controlName))
                continue;

            var adapter = new ControlAdapter();

            if (control.TryGetProperty(_options.OutputAdapterProperty, out var outputElement))
            {
                adapter.Output = JsonSerializer.Deserialize<OutputAdapter>(
                    outputElement.GetRawText(), _jsonOptions);
            }

            if (control.TryGetProperty(_options.InputAdapterProperty, out var inputElement))
            {
                adapter.Input = JsonSerializer.Deserialize<InputAdapter>(
                    inputElement.GetRawText(), _jsonOptions);
            }

            if (adapter.Output != null || adapter.Input != null)
            {
                _adapters[controlName] = adapter;
            }

            if (control.TryGetProperty(_options.ChildControlsProperty, out var childControls))
            {
                ParseControls(childControls);
            }
        }
    }

    private static string? GetControlName(object control)
    {
        try
        {
            return control.GetType().GetProperty("Name")?.GetValue(control)?.ToString();
        }
        catch
        {
            return null;
        }
    }
}

public class ControlAdapter
{
    public OutputAdapter? Output { get; set; }

    public InputAdapter? Input { get; set; }
}

public class OutputAdapter
{
    public string? Strategy { get; set; }

    public Dictionary<string, object>? Parameters { get; set; }
}

public class InputAdapter
{
    public string? Strategy { get; set; }

    public Dictionary<string, object>? Parameters { get; set; }
}
