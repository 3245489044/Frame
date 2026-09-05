
using CodeRLFramework.Pipeline;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Pipeline
{
    public class CodeRLContextTests
    {
        [Fact]
        public void Constructor_WithValidId_ShouldInitialize()
        {
            var context = new CodeRLContext("test-id");

            Assert.NotNull(context);
            Assert.Equal("test-id", context.Id);
        }

        [Fact]
        public void Constructor_WithNullId_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CodeRLContext(null!));
        }

        [Fact]
        public void Constructor_WithEmptyId_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new CodeRLContext(""));
        }

        [Fact]
        public void Constructor_ShouldInitializeDefaultValues()
        {
            var context = new CodeRLContext("test-id");

            Assert.NotNull(context.RewardHistory);
            Assert.Empty(context.RewardHistory);
            Assert.Equal(0, context.Reward);
            Assert.Equal(0, context.BestReward);
            Assert.Null(context.BestCode);
            Assert.Null(context.GeneratedCode);
            Assert.Null(context.GeneratedTokens);
            Assert.Null(context.DecodedFunctionNames);
            Assert.Null(context.FunctionSpace);
            Assert.Null(context.GraphSnapshot);
            Assert.Null(context.NavContext);
            Assert.Null(context.Tokenizer);
            Assert.Null(context.Weights);
            Assert.False(context.IsConverged);
            Assert.Equal(50, context.MaxStepsPerEpisode);
            Assert.Equal(0.01, context.ConvergenceThreshold);
            Assert.Equal(10, context.ConvergenceWindow);
        }

        [Fact]
        public void RewardHistory_ShouldTrackRewards()
        {
            var context = new CodeRLContext("test-id");

            context.RewardHistory.Add(0.5);
            context.RewardHistory.Add(0.6);
            context.RewardHistory.Add(0.7);

            Assert.Equal(3, context.RewardHistory.Count);
            Assert.Equal(0.7, context.RewardHistory.Last());
        }

        [Fact]
        public void BestReward_ShouldOnlyIncrease()
        {
            var context = new CodeRLContext("test-id");

            context.BestReward = 0.5;
            context.BestReward = 0.8;
            context.BestReward = 0.6;

            Assert.Equal(0.8, context.BestReward);
        }

        [Fact]
        public void CreateBasicContext_ShouldInitializeAllProperties()
        {
            var context = TestDataFactory.CreateBasicContext();

            Assert.NotNull(context);
            Assert.Equal("test-basic", context.Id);
            Assert.NotNull(context.FunctionSpace);
            Assert.NotNull(context.GraphSnapshot);
            Assert.NotNull(context.Weights);
            Assert.Equal(50, context.MaxStepsPerEpisode);
            Assert.Equal(0.01, context.ConvergenceThreshold);
            Assert.Equal(10, context.ConvergenceWindow);
        }

        [Fact]
        public void CreateContextWithTokens_ShouldHaveTokenizer()
        {
            var context = TestDataFactory.CreateContextWithTokens();

            Assert.NotNull(context);
            Assert.NotNull(context.Tokenizer);
            Assert.NotNull(context.GeneratedTokens);
            Assert.NotEmpty(context.GeneratedTokens);
        }

        [Fact]
        public void CreateContextWithGeneratedCode_ShouldHaveCode()
        {
            var context = TestDataFactory.CreateContextWithGeneratedCode();

            Assert.NotNull(context);
            Assert.NotNull(context.GeneratedCode);
            Assert.NotEmpty(context.GeneratedCode);
            Assert.NotNull(context.DecodedFunctionNames);
            Assert.NotEmpty(context.DecodedFunctionNames);
        }

        [Fact]
        public void CreateContextWithRewardHistory_ShouldHaveHistory()
        {
            var context = TestDataFactory.CreateContextWithRewardHistory();

            Assert.NotNull(context);
            Assert.NotEmpty(context.RewardHistory);
            Assert.Equal(0.85, context.Reward);
            Assert.Equal(0.85, context.BestReward);
            Assert.NotNull(context.BestCode);
        }
    }
}
