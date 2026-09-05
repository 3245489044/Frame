
using CodeRLFramework.Pipeline;
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.RL;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class RewardEvalSkeletonTests
    {
        private readonly RewardFunction _rewardFunction;

        public RewardEvalSkeletonTests()
        {
            _rewardFunction = new RewardFunction();
        }

        [Fact]
        public void Execute_WithValidContext_ShouldEvaluateReward()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
            Assert.True(result.RewardHistory.Count > 0);
            Assert.Equal(result.Reward, result.RewardHistory.Last(), 6);
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithEmptyGeneratedCode_ShouldThrow()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = "";
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithNullGeneratedCode_ShouldThrow()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithGraphSnapshot_ShouldIncludeGraphReward()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            context.DecodedFunctionNames = new List<string> { "Initialize", "Process", "Finalize" };
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
        }

        [Fact]
        public void Execute_WithNavContext_ShouldIncludeNavReward()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.NavContext = TestDataFactory.CreateBasicNavContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
        }

        [Fact]
        public void Execute_WithAllDimensions_ShouldComputeCompositeReward()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            context.DecodedFunctionNames = new List<string> { "Initialize", "Process", "Finalize" };
            context.NavContext = TestDataFactory.CreateBasicNavContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
            Assert.Contains(result.Reward, result.RewardHistory);
        }

        [Fact]
        public void Execute_ShouldUpdateBestReward_WhenHigher()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.BestReward = -1.0;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.True(result.Reward >= result.BestReward,
                "当前奖励应更新为最佳奖励");
            Assert.Equal(result.Reward, result.BestReward);
            Assert.Equal(result.GeneratedCode, result.BestCode);
        }

        [Fact]
        public void Execute_ShouldNotUpdateBestReward_WhenLower()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.BestReward = 0.99;
            context.BestCode = "previous best code";
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.True(result.BestReward >= result.Reward || result.Reward > 0.99,
                "最佳奖励不应被较低的当前奖励覆盖");
        }

        [Fact]
        public void Execute_WithCustomWeights_ShouldUseCustomWeights()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.Weights!.BaseRewardWeight = 1.0;
            context.Weights.GraphStructureWeight = 0.0;
            context.Weights.NavigationQualityWeight = 0.0;
            context.Weights.PlaceholderIntegrityWeight = 0.0;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
        }

        [Fact]
        public void Execute_WithPlaceholderInCode_ShouldIncludePlaceholderReward()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = TestDataFactory.SampleCodeSnippets.WithPlaceholder;
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
        }

        [Fact]
        public void Execute_WithHighFallbackNav_ShouldReflectInReward()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.NavContext = TestDataFactory.CreateHighFallbackNavContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.InRange(result.Reward, -1.0, 1.0);
        }

        [Fact]
        public void Execute_ShouldRecordInfoInContext()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.True(skelContext.Items.ContainsKey("RewardEvalSkeleton.Info"),
                "应记录奖励评估信息");
            var info = skelContext.Items["RewardEvalSkeleton.Info"]?.ToString();
            Assert.NotNull(info);
            Assert.Contains("奖励", info);
        }

        [Fact]
        public void Constructor_WithNullRewardFunction_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new RewardEvalSkeleton(null!));
        }

        [Fact]
        public void Constructor_WithValidRewardFunction_ShouldSucceed()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);

            Assert.NotNull(skeleton);
            Assert.Equal("RewardEvalSkeleton", skeleton.Name);
        }

        [Fact]
        public void Execute_WithConvergedContext_ShouldNotAffectConvergence()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.IsConverged = true;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.True(result.IsConverged, "收敛状态不应被奖励评估修改");
        }

        [Fact]
        public void Execute_MultipleTimes_ShouldAccumulateRewardHistory()
        {
            var skeleton = new RewardEvalSkeleton(_rewardFunction);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            skeleton.Execute(context, skelContext);
            skeleton.Execute(context, skelContext);
            skeleton.Execute(context, skelContext);

            Assert.True(context.RewardHistory.Count >= 3,
                "多次执行应累积奖励历史");
        }
    }
}
