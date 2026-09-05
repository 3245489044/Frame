
using Modules.Pipeline;

namespace Common.PlaceholderFramework.TemplateExpansion.Abstractions;

public interface IJsonPipeline
{
    string Name { get; }

    IJsonPipeline AddConverter(IJsonConverter converter);

    IJsonPipeline AddConverters(IEnumerable<IJsonConverter> converters);

    IJsonPipeline RemoveConverter<T>() where T : IJsonConverter;

    void Clear();

    PipelineResult Execute(string json, string? sourcePath = null);

    Task<PipelineResult> ExecuteAsync(string filePath);
}
