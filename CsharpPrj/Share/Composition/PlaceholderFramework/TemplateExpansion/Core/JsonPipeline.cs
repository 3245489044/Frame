
using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Pipeline;
using TConfig;
using Plate.JsonMethod.Diagnostics;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Core;

public class JsonPipeline : IJsonPipeline
{
    private readonly Pipeline _pipeline = new();
    private readonly string _name;

    public string Name => _name;

    public IReadOnlyList<IJsonConverter> Converters
        => _pipeline.Stages.OfType<IJsonConverter>().ToList().AsReadOnly();

    public JsonPipeline(string? name = null)
    {
        _name = name ?? "JsonPipeline";
    }

    public IJsonPipeline AddConverter(IJsonConverter converter)
    {
        if (converter == null)
            throw new ArgumentNullException(nameof(converter));

        _pipeline.RegisterStage(converter);
        return this;
    }

    public IJsonPipeline AddConverters(IEnumerable<IJsonConverter> converters)
    {
        if (converters == null)
            throw new ArgumentNullException(nameof(converters));

        foreach (var converter in converters)
            _pipeline.RegisterStage(converter);

        return this;
    }

    public IJsonPipeline RemoveConverter<T>() where T : IJsonConverter
    {
        var remaining = _pipeline.Stages.Where(s => s is not T).ToList();
        _pipeline.Clear();
        foreach (var s in remaining)
            _pipeline.RegisterStage(s);

        return this;
    }

    public void Clear()
    {
        _pipeline.Clear();
    }

    public PipelineResult Execute(string json, string? sourcePath = null)
    {
        if (string.IsNullOrEmpty(json))
        {
            var errors = new ErrorCollector();
            errors.Add("JSON_EMPTY", "Input JSON is null or empty", ErrorLevel.Fatal);
            return PipelineResult.Failed(errors);
        }

        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            var detail = JsonErrorDiagnostics.Enrich(ex, json, typeof(JsonNode));
            var errors = new ErrorCollector();
            errors.Add("JSON_PARSE_ERROR", $"Failed to parse JSON: {detail}", ErrorLevel.Fatal, ex);
            return PipelineResult.Failed(errors);
        }

        if (doc == null)
        {
            var errors = new ErrorCollector();
            errors.Add("JSON_PARSE_NULL", "Parsed JSON document is null", ErrorLevel.Fatal);
            return PipelineResult.Failed(errors);
        }

        var result = _pipeline.Execute(doc, sourcePath);

        return result;
    }

    public async Task<PipelineResult> ExecuteAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var errors = new ErrorCollector();
            errors.Add("FILE_PATH_EMPTY", "File path is null or empty", ErrorLevel.Fatal);
            return PipelineResult.Failed(errors);
        }

        if (!File.Exists(filePath))
        {
            var errors = new ErrorCollector();
            errors.Add("FILE_NOT_FOUND", $"File not found: {filePath}", ErrorLevel.Fatal);
            return PipelineResult.Failed(errors);
        }

        var json = await File.ReadAllTextAsync(filePath);
        return Execute(json, filePath);
    }
}
