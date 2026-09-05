
using CodeRLFramework.Input;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Input
{
    public class WeightConfigTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var config = new WeightConfig();

            Assert.NotNull(config);
            Assert.Equal(0.5, config.CodebaseWeight);
            Assert.Equal(0.5, config.TemplateWeight);
            Assert.Equal(1.0, config.Temperature);
            Assert.Equal(0.1, config.ExplorationRate);
        }

        [Fact]
        public void CustomValues_ShouldBeSet()
        {
            var config = new WeightConfig
            {
                CodebaseWeight = 0.7,
                TemplateWeight = 0.3,
                Temperature = 0.8,
                ExplorationRate = 0.2
            };

            Assert.Equal(0.7, config.CodebaseWeight);
            Assert.Equal(0.3, config.TemplateWeight);
            Assert.Equal(0.8, config.Temperature);
            Assert.Equal(0.2, config.ExplorationRate);
        }

        [Fact]
        public void CodebaseAndTemplateWeight_ShouldSumToOne()
        {
            var config = new WeightConfig();

            Assert.InRange(config.CodebaseWeight + config.TemplateWeight, 0.99, 1.01);
        }

        [Fact]
        public void Temperature_ShouldBePositive()
        {
            var config = new WeightConfig();

            Assert.True(config.Temperature > 0);
        }

        [Fact]
        public void ExplorationRate_ShouldBeBetweenZeroAndOne()
        {
            var config = new WeightConfig();

            Assert.InRange(config.ExplorationRate, 0, 1);
        }
    }
}
