
using CodeRLFramework.Input;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Input
{
    public class CodeRLConfigTests
    {
        [Fact]
        public void GetDefault_ShouldReturnValidConfig()
        {
            var config = CodeRLConfig.GetDefault();

            Assert.NotNull(config);
            Assert.NotNull(config.Pipeline);
            Assert.NotNull(config.Weights);
            Assert.NotNull(config.LLMNavigation);
            Assert.NotNull(config.Skeletons);
            Assert.NotNull(config.Training);
            Assert.False(string.IsNullOrEmpty(config.Pipeline.Name));
        }

        [Fact]
        public void GetDefault_Weights_ShouldHaveValidValues()
        {
            var config = CodeRLConfig.GetDefault();

            Assert.InRange(config.Weights.CodebaseWeight, 0, 1);
            Assert.InRange(config.Weights.TemplateWeight, 0, 1);
            Assert.InRange(config.Weights.Temperature, 0, double.MaxValue);
            Assert.InRange(config.Weights.ExplorationRate, 0, 1);
        }

        [Fact]
        public void GetDefault_Training_ShouldHaveValidValues()
        {
            var config = CodeRLConfig.GetDefault();

            Assert.True(config.Training.MaxEpisodes > 0);
            Assert.True(config.Training.MaxStepsPerEpisode > 0);
            Assert.True(config.Training.ConvergenceThreshold > 0);
            Assert.True(config.Training.ConvergenceWindow > 0);
        }

        [Fact]
        public void CustomConfig_ShouldOverrideDefaults()
        {
            var customConfig = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 10,
                maxStepsPerEpisode: 20,
                temperature: 0.5,
                explorationRate: 0.2);

            Assert.Equal(10, customConfig.Training.MaxEpisodes);
            Assert.Equal(20, customConfig.Training.MaxStepsPerEpisode);
            Assert.Equal(0.5, customConfig.Weights.Temperature);
            Assert.Equal(0.2, customConfig.Weights.ExplorationRate);
        }

        [Fact]
        public void SkeletonConfig_IsEnabled_ShouldReturnTrueForEmptyEnabled()
        {
            var config = CodeRLConfig.GetDefault();

            var isEnabled = config.Skeletons.IsEnabled("CodeExtract");

            Assert.True(isEnabled);
        }

        [Fact]
        public void SkeletonConfig_IsEnabled_WithDisabledList_ShouldReturnFalse()
        {
            var config = CodeRLConfig.GetDefault();
            config.Skeletons.Disabled.Add("CodeExtract");

            var isEnabled = config.Skeletons.IsEnabled("CodeExtract");

            Assert.False(isEnabled);
        }
    }
}
