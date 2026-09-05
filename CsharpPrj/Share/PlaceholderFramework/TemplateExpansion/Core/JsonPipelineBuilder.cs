
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Core;

public class JsonPipelineBuilder
{
    private readonly JsonPipeline _pipeline;

    public JsonPipelineBuilder(string? name = null)
    {
        _pipeline = new JsonPipeline(name);
    }

    public JsonPipelineBuilder Use(IJsonConverter converter)
    {
        _pipeline.AddConverter(converter);
        return this;
    }

    public JsonPipelineBuilder Use(IEnumerable<IJsonConverter> converters)
    {
        _pipeline.AddConverters(converters);
        return this;
    }

    public JsonPipelineBuilder UseExtends(ITemplateStorage storage)
    {
        _pipeline.AddConverter(new Converters.ExtendsConverter(storage));
        return this;
    }

    public JsonPipelineBuilder UseStyleApply(ITemplateStorage storage)
    {
        _pipeline.AddConverter(new Converters.StyleApplyConverter(storage));
        return this;
    }

    public JsonPipelineBuilder UseTemplateExpand(ITemplateStorage storage)
    {
        _pipeline.AddConverter(new Converters.TemplateExpandConverter(storage));
        return this;
    }

    public JsonPipelineBuilder UseDefaultsApply(ITemplateStorage storage)
    {
        _pipeline.AddConverter(new Converters.DefaultsApplyConverter(storage));
        return this;
    }

    public JsonPipelineBuilder UseParameterResolve()
    {
        _pipeline.AddConverter(new Converters.ParameterResolveConverter());
        return this;
    }

    public JsonPipelineBuilder UseConfigMerge()
    {
        _pipeline.AddConverter(new Converters.ConfigMergeConverter());
        return this;
    }

    public JsonPipelineBuilder UseValidation(bool strictMode = true)
    {
        _pipeline.AddConverter(new Converters.ConfigValidateConverter { StrictMode = strictMode });
        return this;
    }

    public JsonPipeline Build()
    {
        return _pipeline;
    }
}
