using JsonUIBeautifier.Models;
using JsonUIBeautifier.Pipeline;
using JsonUIBeautifier.Tools;

namespace JsonUIBeautifier;

public class JsonUIBeautifier
{
    private readonly JsonBeautifierPipeline _pipeline;

    public JsonUIBeautifier(BeautifierConfig? config = null)
    {
        _pipeline = new JsonBeautifierPipeline(config);
    }

    public BeautifyResult Process(string json) => _pipeline.Process(json);

    public BeautifyResult ProcessFile(string inputPath, string? outputPath = null)
        => _pipeline.ProcessFile(inputPath, outputPath);

    public BeautifyReport Analyze(string json) => _pipeline.AnalyzeOnly(json);

    public BeautifyReport AnalyzeFile(string filePath) => _pipeline.AnalyzeFileOnly(filePath);

    public ConfigValidator.ValidationResult Validate(string json)
        => ConfigValidator.Validate(json);

    public ConfigValidator.ValidationResult ValidateFile(string filePath)
        => ConfigValidator.ValidateFile(filePath);

    public string Minify(string json) => JsonMinifier.Minify(json);

    public string PrettyPrint(string json) => JsonMinifier.PrettyPrint(json);

    public BeautifierConfig Config => _pipeline.Config;
}
