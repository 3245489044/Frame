
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.RL;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class PolicyUpdateSkeletonTests
    {
        private readonly PolicyGradient _policyGradient;

        public PolicyUpdateSkeletonTests()
        {
            _policyGradient = new PolicyGradient(10);
        }

        [Fact]
        public void Execute_WithValidContext_ShouldUpdatePolicy()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.True(skelContext.Items.ContainsKey("PolicyUpdateSkeleton.Info"),
                "应记录策略更新信息");
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithNullDecodedFunctionNames_ShouldThrow()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            context.DecodedFunctionNames = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithEmptyDecodedFunctionNames_ShouldThrow()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            context.DecodedFunctionNames = new List<string>();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithZeroReward_ShouldNotThrow()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            context.Reward = 0;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithEmptyRewardHistory_ShouldNotThrow()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.RewardHistory.Clear();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithSingleFunction_ShouldRecordSingleStep()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.DecodedFunctionNames = new List<string> { "Initialize" };
            context.Reward = 0.5;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithMultipleFunctions_ShouldRecordMultipleSteps()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.DecodedFunctionNames = new List<string>
                { "Initialize", "Process", "Validate", "Finalize" };
            context.Reward = 0.8;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_ShouldUpdatePolicyGradientState()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            skeleton.Execute(context, skelContext);

            var avgReward = _policyGradient.AverageReward;
            Assert.True(avgReward >= 0, "平均奖励应为非负");
            Assert.NotNull(_policyGradient.PolicyWeights);
            Assert.NotEmpty(_policyGradient.PolicyWeights);
        }

        [Fact]
        public void Execute_MultipleEpisodes_ShouldUpdatePolicyWeights()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            for (int i = 0; i < 3; i++)
            {
                context.Reward = 0.5 + i * 0.1;
                skeleton.Execute(context, skelContext);
            }

            Assert.NotNull(_policyGradient.PolicyWeights);
            Assert.NotEmpty(_policyGradient.PolicyWeights);
            Assert.All(_policyGradient.PolicyWeights, w =>
                Assert.False(double.IsNaN(w) || double.IsInfinity(w),
                    "策略权重应为有效数值"));
        }

        [Fact]
        public void Execute_WithNegativeReward_ShouldHandleGracefully()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.Reward = -0.5;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithFunctionSpace_ShouldFindFunctionIndex()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            context.FunctionSpace = TestDataFactory.CreateBasicFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Constructor_WithNullPolicyGradient_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PolicyUpdateSkeleton(null!));
        }

        [Fact]
        public void Constructor_WithValidPolicyGradient_ShouldSucceed()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);

            Assert.NotNull(skeleton);
            Assert.Equal("PolicyUpdateSkeleton", skeleton.Name);
        }

        [Fact]
        public void Execute_ShouldRecordInfoWithRewardDetails()
        {
            var skeleton = new PolicyUpdateSkeleton(_policyGradient);
            var context = TestDataFactory.CreateContextWithRewardHistory();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            var info = skelContext.Items["PolicyUpdateSkeleton.Info"]?.ToString();
            Assert.NotNull(info);
            Assert.Contains("策略更新", info);
            Assert.Contains("平均奖励", info);
        }
    }
}
