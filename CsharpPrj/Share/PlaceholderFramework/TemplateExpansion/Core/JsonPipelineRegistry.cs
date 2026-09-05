
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Core;

public static class JsonPipelineRegistry
{
    private static readonly object _lock = new();
    private static ITemplateStorage? _storage;
    private static IJsonPipeline? _defaultPipeline;
    private static readonly Dictionary<string, IJsonPipeline> _namedPipelines = new();
    private static bool _configured;

    public static ITemplateStorage Storage
    {
        get
        {
            if (_storage == null)
                throw new InvalidOperationException(
                    "JsonPipelineRegistry has not been configured. Call Configure() first.");
            return _storage;
        }
    }

    public static bool IsConfigured => _configured;

    public static void Configure(
        string stylesPath,
        string compositesPath,
        Action<JsonPipelineBuilder>? configurePipeline = null)
    {
        Configure(new[] { (stylesPath, compositesPath) }, configurePipeline);
    }

    public static void Configure(
        IEnumerable<(string stylesPath, string compositesPath)> templatePaths,
        Action<JsonPipelineBuilder>? configurePipeline = null)
    {
        lock (_lock)
        {
            var pathList = templatePaths?.ToList();
            if (pathList == null || pathList.Count == 0)
                throw new ArgumentException("至少需要一组模板路径。", nameof(templatePaths));

            foreach (var (stylesPath, compositesPath) in pathList)
            {
                ValidateFileExists(stylesPath, "styles.json");
                ValidateFileExists(compositesPath, "composites.json");
            }

            _storage = new Storage.JsonFileTemplateStorage();
            _storage.Initialize(pathList);

            var builder = new JsonPipelineBuilder("Default");
            if (configurePipeline != null)
            {
                configurePipeline(builder);
            }
            else
            {
                builder.UseExtends(_storage)
                       .UseStyleApply(_storage)
                       .UseTemplateExpand(_storage)
                       .UseDefaultsApply(_storage)
                       .UseParameterResolve()
                       .UseValidation(false);
            }

            _defaultPipeline = builder.Build();
            _configured = true;
        }
    }

    private static void ValidateFileExists(string filePath, string fileDescription)
    {
        if (!File.Exists(filePath))
        {
            PlaceholderFramework.Diagnostics.PlaceholderFrameworkDiagnosticContext.Record("jsonpipeline.file.missing", new { fileDescription, filePath });
            throw new FileNotFoundException($"{fileDescription} not found: {filePath}", filePath);
        }
    }

    public static IJsonPipeline GetDefaultPipeline()
    {
        if (_defaultPipeline == null)
            throw new InvalidOperationException(
                "JsonPipelineRegistry has not been configured. Call Configure() first.");
        return _defaultPipeline;
    }

    public static IJsonPipeline GetOrCreatePipeline(string name, Func<JsonPipelineBuilder, JsonPipeline> factory)
    {
        lock (_lock)
        {
            if (!_namedPipelines.TryGetValue(name, out var pipeline))
            {
                var builder = new JsonPipelineBuilder(name);
                pipeline = factory(builder);
                _namedPipelines[name] = pipeline;
            }
            return pipeline;
        }
    }

    public static void RegisterPipeline(string name, IJsonPipeline pipeline)
    {
        lock (_lock)
        {
            _namedPipelines[name] = pipeline;
        }
    }

    public static Modules.Pipeline.PipelineResult Process(string json, string? sourcePath = null)
    {
        return GetDefaultPipeline().Execute(json, sourcePath);
    }

    public static async Task<Modules.Pipeline.PipelineResult> ProcessFileAsync(string filePath)
    {
        return await GetDefaultPipeline().ExecuteAsync(filePath);
    }

    public static void Reset()
    {
        lock (_lock)
        {
            _storage = null;
            _defaultPipeline = null;
            _namedPipelines.Clear();
            _configured = false;
        }
    }
}
