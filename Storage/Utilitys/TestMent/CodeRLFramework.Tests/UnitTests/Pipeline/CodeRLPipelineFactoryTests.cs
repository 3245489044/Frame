
using CodeRLFramework.Pipeline;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Pipeline
{
    public class CodeRLPipelineFactoryTests : TestBase
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldSucceed()
        {
            var factory = CreateDefaultFactory();

            Assert.NotNull(factory);
        }

        [Fact]
        public void Constructor_WithNullExtractor_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CodeRLPipelineFactory(
                    extractor: null!,
                    agent: Agent,
                    converter: Converter,
                    rewardFunction: RewardFunction,
                    policyGradient: PolicyGradient,
                    functionSpace: FunctionSpace,
                    tokenizer: Tokenizer,
                    actionCount: 10));
        }

        [Fact]
        public void Constructor_WithNullAgent_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CodeRLPipelineFactory(
                    extractor: new VectorExtractor(),
                    agent: null!,
                    converter: Converter,
                    rewardFunction: RewardFunction,
                    policyGradient: PolicyGradient,
                    functionSpace: FunctionSpace,
                    tokenizer: Tokenizer,
                    actionCount: 10));
        }

        [Fact]
        public void Constructor_WithZeroActionCount_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CodeRLPipelineFactory(
                    extractor: new VectorExtractor(),
                    agent: Agent,
                    converter: Converter,
                    rewardFunction: RewardFunction,
                    policyGradient: PolicyGradient,
                    functionSpace: FunctionSpace,
                    tokenizer: Tokenizer,
                    actionCount: 0));
        }

        [Fact]
        public void Constructor_WithNegativeActionCount_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CodeRLPipelineFactory(
                    extractor: new VectorExtractor(),
                    agent: Agent,
                    converter: Converter,
                    rewardFunction: RewardFunction,
                    policyGradient: PolicyGradient,
                    functionSpace: FunctionSpace,
                    tokenizer: Tokenizer,
                    actionCount: -1));
        }

        [Fact]
        public void CreateTrainingPipeline_ShouldReturnValidPipeline()
        {
            var factory = CreateDefaultFactory();

            using var pipeline = factory.CreateTrainingPipeline();

            Assert.NotNull(pipeline);
            Assert.Equal("TrainingPipeline", pipeline.Name);
        }

        [Fact]
        public void CreateSingleEpisodePipeline_ShouldReturnValidPipeline()
        {
            var factory = CreateDefaultFactory();

            using var pipeline = factory.CreateSingleEpisodePipeline();

            Assert.NotNull(pipeline);
            Assert.Equal("SingleEpisodePipeline", pipeline.Name);
        }

        [Fact]
        public void CreateTrainingPipeline_ShouldExecuteEpisode()
        {
            var factory = CreateDefaultFactory();
            using var pipeline = factory.CreateTrainingPipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public void CreateSingleEpisodePipeline_ShouldExecuteEpisode()
        {
            var factory = CreateDefaultFactory();
            using var pipeline = factory.CreateSingleEpisodePipeline();
            var context = TestDataFactory.CreateBasicContext();

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public void CreateTrainingPipeline_WithComplexSpace_ShouldExecute()
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
        public void CreateSingleEpisodePipeline_WithComplexSpace_ShouldExecute()
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
            using var pipeline = factory.CreateSingleEpisodePipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = space;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void CreateTrainingPipeline_WithEmptySpace_ShouldExecute()
        {
            var (space, _, agent, converter) = CreateEmptyDependencies();
            var factory = new CodeRLPipelineFactory(
                extractor: new VectorExtractor(),
                agent: agent,
                converter: converter,
                rewardFunction: RewardFunction,
                policyGradient: PolicyGradient,
                functionSpace: space,
                tokenizer: new CodeTokenizer(space),
                actionCount: 5);
            using var pipeline = factory.CreateTrainingPipeline();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = space;

            var result = pipeline.ExecuteEpisode(context);

            Assert.NotNull(result);
        }
    }
}
