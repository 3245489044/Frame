
using System.Text.Json;
using CodeRLFramework.Input;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Input
{
    public class CodeRLConfigSerializationTests
    {
        [Fact]
        public void Serialize_DefaultConfig_ShouldProduceValidJson()
        {
            var config = new CodeRLConfig();

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Assert.NotNull(json);
            Assert.Contains("Training", json);
            Assert.Contains("Skeletons", json);
            Assert.Contains("Weights", json);
        }

        [Fact]
        public void Deserialize_ValidJson_ShouldRestoreConfig()
        {
            var json = @"{
                ""Training"": {
                    ""MaxEpisodes"": 50,
                    ""MaxStepsPerEpisode"": 25,
                    ""ConvergenceThreshold"": 0.005,
                    ""ConvergenceWindow"": 5
                },
                ""Skeletons"": {
                    ""Disabled"": [""CodeExtract""]
                },
                ""Weights"": {
                    ""CodebaseWeight"": 0.7,
                    ""TemplateWeight"": 0.3,
                    ""Temperature"": 0.8,
                    ""ExplorationRate"": 0.2
                },
                ""LLMNavigation"": {
                    ""Enabled"": true,
                    ""MaxDepth"": 10,
                    ""MaxFallbackCount"": 5
                }
            }";

            var config = JsonSerializer.Deserialize<CodeRLConfig>(json);

            Assert.NotNull(config);
            Assert.NotNull(config.Training);
            Assert.Equal(50, config.Training.MaxEpisodes);
            Assert.Equal(25, config.Training.MaxStepsPerEpisode);
            Assert.Equal(0.005, config.Training.ConvergenceThreshold);
            Assert.Equal(5, config.Training.ConvergenceWindow);
            Assert.NotNull(config.Skeletons);
            Assert.Contains("CodeExtract", config.Skeletons.Disabled);
            Assert.NotNull(config.Weights);
            Assert.Equal(0.7, config.Weights.CodebaseWeight);
            Assert.Equal(0.3, config.Weights.TemplateWeight);
            Assert.Equal(0.8, config.Weights.Temperature);
            Assert.Equal(0.2, config.Weights.ExplorationRate);
            Assert.NotNull(config.LLMNavigation);
            Assert.True(config.LLMNavigation.Enabled);
            Assert.Equal(10, config.LLMNavigation.MaxDepth);
            Assert.Equal(5, config.LLMNavigation.MaxFallbackCount);
        }

        [Fact]
        public void Deserialize_MinimalJson_ShouldUseDefaults()
        {
            var json = @"{}";

            var config = JsonSerializer.Deserialize<CodeRLConfig>(json);

            Assert.NotNull(config);
            Assert.NotNull(config.Training);
            Assert.Equal(100, config.Training.MaxEpisodes);
            Assert.Equal(50, config.Training.MaxStepsPerEpisode);
            Assert.NotNull(config.Skeletons);
            Assert.Empty(config.Skeletons.Disabled);
            Assert.NotNull(config.Weights);
            Assert.Equal(0.5, config.Weights.CodebaseWeight);
            Assert.Equal(0.5, config.Weights.TemplateWeight);
            Assert.NotNull(config.LLMNavigation);
            Assert.False(config.LLMNavigation.Enabled);
        }

        [Fact]
        public void SerializeDeserialize_RoundTrip_ShouldPreserveValues()
        {
            var original = new CodeRLConfig
            {
                Training = new TrainingConfig
                {
                    MaxEpisodes = 30,
                    MaxStepsPerEpisode = 15
                },
                Weights = new WeightConfig
                {
                    CodebaseWeight = 0.6,
                    TemplateWeight = 0.4
                }
            };

            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<CodeRLConfig>(json);

            Assert.NotNull(restored);
            Assert.Equal(original.Training.MaxEpisodes, restored.Training.MaxEpisodes);
            Assert.Equal(original.Training.MaxStepsPerEpisode, restored.Training.MaxStepsPerEpisode);
            Assert.Equal(original.Weights.CodebaseWeight, restored.Weights.CodebaseWeight);
            Assert.Equal(original.Weights.TemplateWeight, restored.Weights.TemplateWeight);
        }
    }
}
