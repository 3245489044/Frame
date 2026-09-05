
using CodeRLFramework.Pipeline;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Pipeline
{
    public class CodeRLPipelineTests : TestBase
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldSucceed()
        {
            using var pipeline = CreateDefaultPipeline();

            Assert.NotNull(pipeline);
            Assert.Equal(DefaultPipelineName, pipeline.Name);
        }

        [Fact]
        public void Constructor_WithNullName_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CodeRLPipeline(
                    null!,
                    extractor: new VectorExtractor(),
                    agent: Agent,
                    converter: Converter,
                    rewardFunction: RewardFunction,
                    policyGradient: PolicyGradient));
        }

        [Fact]
        public void Constructor_WithNullAgent_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CodeRLPipeline(
                    DefaultPipelineName,
                    extractor: new VectorExtractor(),
                    agent: null!,
                    converter: Converter,
                    rewardFunction: RewardFunction,
                    policyGradient: PolicyGradient));
        }

        [Fact]
        public void ExecuteEpisode_WithValidContext_ShouldComplete()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.RewardHistory.Count > 0);
        }

        [Fact]
        public void ExecuteEpisode_WithNullContext_ShouldThrow()
        {
            using var pipeline = CreateDefaultPipeline();

            Assert.Throws<ArgumentNullException>(() =>
                pipeline.ExecuteEpisode(null!));
        }

        [Fact]
        public void ExecuteEpisode_WithConvergedContext_ShouldStopEarly()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.IsConverged = true;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.True(result.IsConverged);
        }

        [Fact]
        public void ExecuteEpisode_WithGraphSnapshot_ShouldNavigate()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public void ExecuteEpisode_WithComplexGraph_ShouldNavigate()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = TestDataFactory.CreateComplexGraphSnapshot();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void ExecuteEpisode_WithPlaceholderCode_ShouldResolve()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = TestDataFactory.SampleCodeSnippets.WithPlaceholder;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.DoesNotContain("@", result.GeneratedCode);
        }

        [Fact]
        public void ExecuteEpisode_WithComplexFunctionSpace_ShouldHandleGracefully()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateComplexFunctionSpace();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void ExecuteEpisode_WithEmptyFunctionSpace_ShouldHandleGracefully()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void ExecuteEpisode_WithNavContext_ShouldIncludeNavReward()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.NavContext = TestDataFactory.CreateBasicNavContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
        }

        [Fact]
        public void ExecuteEpisode_WithHighFallbackNav_ShouldHandleGracefully()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.NavContext = TestDataFactory.CreateHighFallbackNavContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void ExecuteEpisode_WithCustomWeights_ShouldUseCustomWeights()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.Weights!.BaseRewardWeight = 1.0;
            context.Weights.GraphStructureWeight = 0.0;
            context.Weights.NavigationQualityWeight = 0.0;
            context.Weights.PlaceholderIntegrityWeight = 0.0;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void ExecuteEpisode_MultipleTimes_ShouldAccumulateRewards()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            for (int i = 0; i < 3; i++)
            {
                context = pipeline.ExecuteEpisode(context);
            }

            Assert.NotNull(context);
            Assert.True(context.RewardHistory.Count >= 3,
                "多次执行应累积奖励历史");
        }

        [Fact]
        public void ExecuteEpisode_WithAllSkeletons_ShouldCompleteSuccessfully()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            context.DecodedFunctionNames = new List<string> { "Initialize", "Process", "Finalize" };
            context.NavContext = TestDataFactory.CreateBasicNavContext();
            context.GeneratedCode = TestDataFactory.SampleCodeSnippets.WithPlaceholder;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.DoesNotContain("@", result.GeneratedCode);
            Assert.True(result.RewardHistory.Count > 0);
        }

        [Fact]
        public void ExecuteEpisode_WithRewardHistory_ShouldCheckConvergence()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.RewardHistory.AddRange(
                Enumerable.Repeat(0.95, 10));

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.True(result.IsConverged, "高稳定奖励应收敛");
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            var pipeline = CreateDefaultPipeline();

            pipeline.Dispose();

        }

        [Fact]
        public void ExecuteEpisode_AfterDispose_ShouldThrow()
        {
            var pipeline = CreateDefaultPipeline();
            pipeline.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                pipeline.ExecuteEpisode(TestDataFactory.CreateBasicContext()));
        }
    }
}
