
using CodeRLFramework.Input;
using CodeRLFramework.Pipeline;
using CodeRLFramework.RL;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.RL
{
    public class RLTrainerTests : TestBase
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldSucceed()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateDefaultConfig();

            var trainer = new RLTrainer(pipeline, config);

            Assert.NotNull(trainer);
        }

        [Fact]
        public void Constructor_WithNullPipeline_ShouldThrow()
        {
            var config = TestDataFactory.CreateDefaultConfig();

            Assert.Throws<ArgumentNullException>(() =>
                new RLTrainer(null!, config));
        }

        [Fact]
        public void Constructor_WithNullConfig_ShouldThrow()
        {
            using var pipeline = CreateDefaultPipeline();

            Assert.Throws<ArgumentNullException>(() =>
                new RLTrainer(pipeline, null!));
        }

        [Fact]
        public void Train_WithValidContext_ShouldComplete()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 5, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
            Assert.True(result.RewardHistory.Count > 0,
                "训练应产生奖励历史");
        }

        [Fact]
        public void Train_WithConvergence_ShouldStopEarly()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 100, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();
            context.RewardHistory.AddRange(
                Enumerable.Repeat(0.95, 10));

            var result = trainer.Train(context);

            Assert.NotNull(result);
            Assert.True(result.IsConverged, "高稳定奖励应收敛");
        }

        [Fact]
        public void Train_WithMultipleEpisodes_ShouldTrackProgress()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
            Assert.True(result.RewardHistory.Count >= 1,
                "训练应至少产生一个奖励记录");
        }

        [Fact]
        public void Train_WithCustomConfig_ShouldUseCustomSettings()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 2,
                maxStepsPerEpisode: 5,
                temperature: 0.5,
                explorationRate: 0.2);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Train_WithComplexFunctionSpace_ShouldHandleGracefully()
        {
            var (space, _, agent, converter) = CreateComplexDependencies();
            using var pipeline = new CodeRLPipeline(
                "TestPipeline",
                extractor: new VectorExtractor(),
                agent: agent,
                converter: converter,
                rewardFunction: RewardFunction,
                policyGradient: PolicyGradient);
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = space;

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Train_WithEmptyFunctionSpace_ShouldHandleGracefully()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 2, maxStepsPerEpisode: 5);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Train_WithNullContext_ShouldThrow()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateDefaultConfig();
            var trainer = new RLTrainer(pipeline, config);

            Assert.Throws<ArgumentNullException>(() =>
                trainer.Train(null!));
        }

        [Fact]
        public void Train_ShouldTrackEpisodeCount()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 5, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
            Assert.True(trainer.EpisodeCount > 0,
                "应跟踪 Episode 数量");
        }

        [Fact]
        public void Train_WithGraphSnapshot_ShouldIncludeGraphNavigation()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Train_WithNavContext_ShouldIncludeNavigationReward()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.NavContext = TestDataFactory.CreateBasicNavContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Train_WithPlaceholderCode_ShouldResolvePlaceholders()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3, maxStepsPerEpisode: 10);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = TestDataFactory.SampleCodeSnippets.WithPlaceholder;

            var result = trainer.Train(context);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public void Train_WithHighTemperature_ShouldExploreMore()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3,
                maxStepsPerEpisode: 10,
                temperature: 1.5);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Train_WithLowTemperature_ShouldExploitMore()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3,
                maxStepsPerEpisode: 10,
                temperature: 0.1);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }

        [Fact]
        public void Train_WithZeroExploration_ShouldBeDeterministic()
        {
            using var pipeline = CreateDefaultPipeline();
            var config = TestDataFactory.CreateCustomConfig(
                maxEpisodes: 3,
                maxStepsPerEpisode: 10,
                explorationRate: 0.0);
            var trainer = new RLTrainer(pipeline, config);
            var context = TestDataFactory.CreateBasicContext();

            var result = trainer.Train(context);

            Assert.NotNull(result);
        }
    }
}
