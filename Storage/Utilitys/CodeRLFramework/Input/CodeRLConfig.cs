
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeRLFramework.Input
{
    public class CodeRLConfig
    {
        [JsonPropertyName("pipeline")]
        public PipelineConfig Pipeline { get; set; } = new();

        [JsonPropertyName("weights")]
        public WeightConfig Weights { get; set; } = new();

        [JsonPropertyName("llmNavigation")]
        public LLMNavigationConfig LLMNavigation { get; set; } = new();

        [JsonPropertyName("skeletons")]
        public SkeletonConfig Skeletons { get; set; } = new();

        [JsonPropertyName("training")]
        public TrainingConfig Training { get; set; } = new();

        public bool IsValid()
        {
            return Pipeline != null
                && Weights != null
                && Weights.IsValid()
                && LLMNavigation != null
                && Skeletons != null
                && Training != null
                && Training.MaxEpisodes >= 0
                && Training.MaxStepsPerEpisode > 0;
        }

        public static CodeRLConfig FromJson(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            return JsonSerializer.Deserialize<CodeRLConfig>(json, options)
                ?? throw new InvalidOperationException("无法解析 JSON 配置");
        }

        public static CodeRLConfig FromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"配置文件未找到: {filePath}", filePath);

            string json = File.ReadAllText(filePath);
            return FromJson(json);
        }

        public static CodeRLConfig GetDefault()
        {
            return new CodeRLConfig();
        }
    }

    public class PipelineConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "CodeRL-Training";

        [JsonPropertyName("enableLLMNavigation")]
        public bool EnableLLMNavigation { get; set; } = true;

        [JsonPropertyName("enableGraphReward")]
        public bool EnableGraphReward { get; set; } = true;
    }

    public class LLMNavigationConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("confidenceThreshold")]
        public double ConfidenceThreshold { get; set; } = 0.3;

        [JsonPropertyName("maxNavSteps")]
        public int MaxNavSteps { get; set; } = 20;

        [JsonPropertyName("promptTemplate")]
        public string? PromptTemplate { get; set; }
    }

    public class SkeletonConfig
    {
        [JsonPropertyName("enabled")]
        public List<string> Enabled { get; set; } = new();

        [JsonPropertyName("disabled")]
        public List<string> Disabled { get; set; } = new();

        public bool IsEnabled(string skeletonName)
        {
            if (Disabled.Contains(skeletonName))
                return false;
            if (Enabled.Count > 0)
                return Enabled.Contains(skeletonName);
            return true;
        }
    }

    public class TrainingConfig
    {
        [JsonPropertyName("maxEpisodes")]
        public int MaxEpisodes { get; set; } = 100;

        [JsonPropertyName("maxStepsPerEpisode")]
        public int MaxStepsPerEpisode { get; set; } = 50;

        [JsonPropertyName("convergenceThreshold")]
        public double ConvergenceThreshold { get; set; } = 0.01;

        [JsonPropertyName("convergenceWindow")]
        public int ConvergenceWindow { get; set; } = 10;

        [JsonPropertyName("codebasePath")]
        public string? CodebasePath { get; set; }

        [JsonPropertyName("templateCode")]
        public string? TemplateCode { get; set; }

        [JsonPropertyName("testCode")]
        public string? TestCode { get; set; }
    }
}
