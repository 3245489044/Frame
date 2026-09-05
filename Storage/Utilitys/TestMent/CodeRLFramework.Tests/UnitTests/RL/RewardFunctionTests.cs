
using CodeRLFramework.RL;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.RL
{
    public class RewardFunctionTests
    {
        [Fact]
        public void Calculate_WithValidCode_ShouldReturnReward()
        {
            var rewardFunc = new RewardFunction();
            var code = "public void Test() { Initialize(); Process(1); Finalize(); }";

            var reward = rewardFunc.Calculate(code, "", null, null);

            Assert.InRange(reward, -1.0, 1.0);
        }

        [Fact]
        public void Calculate_WithEmptyCode_ShouldReturnNegative()
        {
            var rewardFunc = new RewardFunction();

            var reward = rewardFunc.Calculate("", "", null, null);

            Assert.True(reward <= 0, "空代码应产生非正奖励");
        }

        [Fact]
        public void Calculate_WithNullCode_ShouldReturnNegative()
        {
            var rewardFunc = new RewardFunction();

            var reward = rewardFunc.Calculate(null!, "", null, null);

            Assert.True(reward <= 0, "null 代码应产生非正奖励");
        }

        [Fact]
        public void CalculateGraphStructureReward_WithValidCode_ShouldReturnPositive()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            var code = "public void Test() { Initialize(); Process(1); Finalize(); }";

            var reward = rewardFunc.CalculateGraphStructureReward(code, graphSnapshot);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward >= 0, $"图结构奖励应为非负值，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateGraphStructureReward_WithInvalidCode_ShouldReturnLower()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            var code = "public void Test() { NonExistentFunc(); AnotherBadCall(); }";

            var reward = rewardFunc.CalculateGraphStructureReward(code, graphSnapshot);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward <= 0.3, $"无效引用的图结构奖励应较低，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateGraphStructureReward_WithEmptyCode_ShouldReturnNegative()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();

            var reward = rewardFunc.CalculateGraphStructureReward("", graphSnapshot);

            Assert.Equal(-0.5, reward);
        }

        [Fact]
        public void CalculateGraphStructureReward_WithNullGraph_ShouldReturnZero()
        {
            var rewardFunc = new RewardFunction();

            var reward = rewardFunc.CalculateGraphStructureReward("public void Test() { }", null!);

            Assert.Equal(0, reward);
        }

        [Fact]
        public void CalculateNavigationQualityReward_WithGoodPath_ShouldReturnPositive()
        {
            var rewardFunc = new RewardFunction();
            var navContext = TestDataFactory.CreateBasicNavContext();

            var reward = rewardFunc.CalculateNavigationQualityReward(navContext);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward >= 0, $"良好导航路径的奖励应为非负值，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateNavigationQualityReward_WithHighFallback_ShouldReturnLower()
        {
            var rewardFunc = new RewardFunction();
            var navContext = TestDataFactory.CreateHighFallbackNavContext();

            var reward = rewardFunc.CalculateNavigationQualityReward(navContext);

            Assert.InRange(reward, -1.0, 1.0);
            Assert.True(reward <= 0.5, $"高回退率的导航奖励应较低，实际为 {reward:F4}");
        }

        [Fact]
        public void CalculateNavigationQualityReward_WithEmptyContext_ShouldReturnZero()
        {
            var rewardFunc = new RewardFunction();

            var reward = rewardFunc.CalculateNavigationQualityReward(null!);

            Assert.Equal(0, reward);
        }

        [Fact]
        public void Calculate_WithGraphSnapshot_ShouldIncludeGraphReward()
        {
            var rewardFunc = new RewardFunction();
            var graphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            var code = "public void Test() { Initialize(); Process(1); Finalize(); }";

            var rewardWithGraph = rewardFunc.Calculate(code, "", graphSnapshot, null);
            var rewardWithoutGraph = rewardFunc.Calculate(code, "", null, null);

            Assert.NotEqual(rewardWithGraph, rewardWithoutGraph);
        }

        [Fact]
        public void Calculate_WithNavContext_ShouldIncludeNavReward()
        {
            var rewardFunc = new RewardFunction();
            var navContext = TestDataFactory.CreateBasicNavContext();
            var code = "public void Test() { Initialize(); Process(1); Finalize(); }";

            var rewardWithNav = rewardFunc.Calculate(code, "", null, navContext);
            var rewardWithoutNav = rewardFunc.Calculate(code, "", null, null);

            Assert.NotEqual(rewardWithNav, rewardWithoutNav);
        }
    }
}
