
using CodeRLFramework.Input;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Input
{
    public class PipelineConfigTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var config = new PipelineConfig();

            Assert.NotNull(config);
            Assert.False(string.IsNullOrEmpty(config.Name));
            Assert.NotNull(config.Skeletons);
            Assert.NotEmpty(config.Skeletons);
        }

        [Fact]
        public void CustomName_ShouldBeSet()
        {
            var config = new PipelineConfig { Name = "CustomPipeline" };

            Assert.Equal("CustomPipeline", config.Name);
        }

        [Fact]
        public void Skeletons_ShouldContainDefaultList()
        {
            var config = new PipelineConfig();

            Assert.Contains("CodeExtract", config.Skeletons);
            Assert.Contains("Tokenize", config.Skeletons);
            Assert.Contains("Vectorize", config.Skeletons);
            Assert.Contains("GraphValidate", config.Skeletons);
            Assert.Contains("CodeGen", config.Skeletons);
            Assert.Contains("Convert", config.Skeletons);
            Assert.Contains("RewardEval", config.Skeletons);
            Assert.Contains("PolicyUpdate", config.Skeletons);
            Assert.Contains("ConvergenceCheck", config.Skeletons);
        }

        [Fact]
        public void CustomSkeletons_ShouldOverride()
        {
            var config = new PipelineConfig
            {
                Skeletons = new List<string> { "CustomSkeleton" }
            };

            Assert.Single(config.Skeletons);
            Assert.Equal("CustomSkeleton", config.Skeletons[0]);
        }
    }
}
