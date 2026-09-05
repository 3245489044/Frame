
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class ConvergenceCheckSkeletonTests
    {
        [Fact]
        public void Execute_NotConverged_WhenInsufficientHistory()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.RewardHistory.AddRange(new[] { 0.5, 0.6, 0.55 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.False(result.IsConverged);
        }

        [Fact]
        public void Execute_Converged_WhenStable()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.01;
            context.RewardHistory.AddRange(new[] { 0.85, 0.86, 0.85, 0.86, 0.85 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.True(result.IsConverged);
        }

        [Fact]
        public void Execute_NotConverged_WhenUnstable()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.01;
            context.RewardHistory.AddRange(new[] { 0.9, 0.5, 0.8, 0.4, 0.7 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.False(result.IsConverged);
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new ConvergenceCheckSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithEmptyRewardHistory_ShouldNotConverge()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.False(result.IsConverged);
        }

        [Fact]
        public void Execute_WithLargeWindow_ShouldHandleGracefully()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 100;
            context.RewardHistory.AddRange(new[] { 0.5, 0.6, 0.55 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.False(result.IsConverged);
        }

        [Fact]
        public void Execute_WithZeroThreshold_ShouldConvergeWhenPerfectlyStable()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.0;
            context.RewardHistory.AddRange(new[] { 0.85, 0.85, 0.85, 0.85, 0.85 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.True(result.IsConverged);
        }

        [Fact]
        public void Execute_WithExactWindowSize_ShouldCheckConvergence()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 3;
            context.ConvergenceThreshold = 0.1;
            context.RewardHistory.AddRange(new[] { 0.8, 0.81, 0.79 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.True(result.IsConverged, "刚好达到窗口大小应检查收敛");
        }

        [Fact]
        public void Execute_WithOneLessThanWindow_ShouldNotConverge()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.01;
            context.RewardHistory.AddRange(new[] { 0.85, 0.86, 0.85, 0.86 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.False(result.IsConverged, "未达到窗口大小不应收敛");
        }

        [Fact]
        public void Execute_WithNegativeRewards_ShouldCheckConvergenceByVariance()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.05;
            context.RewardHistory.AddRange(new[] { -0.5, -0.49, -0.51, -0.5, -0.48 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.True(result.IsConverged, "负奖励但波动小也应收敛");
        }

        [Fact]
        public void Execute_WithHighAverageReward_ShouldConverge()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.05;
            context.RewardHistory.AddRange(new[] { 0.9, 0.91, 0.89, 0.92, 0.9 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.True(result.IsConverged, "高平均奖励且波动小应收敛");
        }

        [Fact]
        public void Execute_ShouldRecordConvergenceInfo()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.01;
            context.RewardHistory.AddRange(new[] { 0.85, 0.86, 0.85, 0.86, 0.85 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.True(skelContext.Items.ContainsKey("ConvergenceCheckSkeleton.Info"),
                "应记录收敛检查信息");
            var info = skelContext.Items["ConvergenceCheckSkeleton.Info"]?.ToString();
            Assert.NotNull(info);
            Assert.Contains("收敛", info);
        }

        [Fact]
        public void Execute_ShouldRecordNotConvergedInfo()
        {
            var skeleton = new ConvergenceCheckSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.ConvergenceWindow = 5;
            context.ConvergenceThreshold = 0.01;
            context.RewardHistory.AddRange(new[] { 0.9, 0.5, 0.8, 0.4, 0.7 });
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.True(skelContext.Items.ContainsKey("ConvergenceCheckSkeleton.Info"),
                "应记录未收敛信息");
            var info = skelContext.Items["ConvergenceCheckSkeleton.Info"]?.ToString();
            Assert.NotNull(info);
            Assert.Contains("未收敛", info);
        }

        [Fact]
        public void Constructor_ShouldSucceed()
        {
            var skeleton = new ConvergenceCheckSkeleton();

            Assert.NotNull(skeleton);
            Assert.Equal("ConvergenceCheckSkeleton", skeleton.Name);
        }
    }
}
