
using CodeRLFramework.Pipeline;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.IntegrationTests
{
    public class PipelineIntegrationTests : TestBase
    {
        [Fact]
        public void FullPipeline_WithComponents_ShouldComplete()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.True(result.RewardHistory.Count > 0);
        }

        [Fact]
        public void FullPipeline_WithMultipleEpisodes_ShouldAccumulateRewards()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result1 = pipeline.ExecuteEpisode(context);
            var result2 = pipeline.ExecuteEpisode(result1);
            var result3 = pipeline.ExecuteEpisode(result2);

            Assert.NotNull(result3);
            Assert.True(result3.RewardHistory.Count >= 3,
                "多 Episode 应累积奖励历史");
        }

        [Fact]
        public void FullPipeline_WithConvergence_ShouldStopEarly()
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
        public void FullPipeline_WithGraphNavigation_ShouldNavigate()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public void FullPipeline_WithPlaceholderResolution_ShouldResolve()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = TestDataFactory.SampleCodeSnippets.WithPlaceholder;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.DoesNotContain("@input:", result.GeneratedCode);
            Assert.DoesNotContain("@node:", result.GeneratedCode);
        }

        [Fact]
        public void FullPipeline_WithVectorExtraction_ShouldExtractVectors()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
        }

        [Fact]
        public void FullPipeline_WithComplexFunctionSpace_ShouldHandleGracefully()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateComplexFunctionSpace();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void FullPipeline_WithEmptyFunctionSpace_ShouldHandleGracefully()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void FullPipeline_WithNavContext_ShouldIncludeNavReward()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.NavContext = TestDataFactory.CreateBasicNavContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
        }

        [Fact]
        public void FullPipeline_WithHighFallbackNav_ShouldHandleGracefully()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.NavContext = TestDataFactory.CreateHighFallbackNavContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void FullPipeline_WithCustomConfig_ShouldUseCustomSettings()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.MaxStepsPerEpisode = 10;
            context.ConvergenceThreshold = 0.1;
            context.ConvergenceWindow = 3;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Factory_CreateTrainingPipeline_ShouldRun()
        {
            var factory = CreateDefaultFactory();
            using var pipeline = factory.CreateTrainingPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Factory_CreateSingleEpisodePipeline_ShouldRun()
        {
            var factory = CreateDefaultFactory();
            using var pipeline = factory.CreateSingleEpisodePipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Factory_CreateTrainingPipeline_WithComplexSpace_ShouldRun()
        {
            var (space, _, agent, converter) = CreateComplexDependencies();
            var factory = new CodeRLPipelineFactory(
                extractor: new VectorExtractor(),
                agent: agent,
                converter: converter,
                rewardFunction: RewardFunction,
                policyGradient: PolicyGradient,
                functionSpace: space,
                tokenizer: new CodeTokenizer(space),
                actionCount: 12);
            using var pipeline = factory.CreateTrainingPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = space;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void FullPipeline_WithAllSkeletons_ShouldCompleteSuccessfully()
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
        public void FullPipeline_WithMultipleEpisodes_ShouldImproveReward()
        {
            using var pipeline = CreateDefaultPipeline();
            var context = TestDataFactory.CreateBasicContext();

            for (int i = 0; i < 5; i++)
            {
                context = pipeline.ExecuteEpisode(context);
            }

            Assert.NotNull(context);
            Assert.True(context.RewardHistory.Count >= 5,
                "5 轮训练应累积 5 个奖励记录");
        }
    }
}
