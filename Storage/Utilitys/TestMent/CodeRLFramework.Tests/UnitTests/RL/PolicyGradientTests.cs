
using CodeRLFramework.RL;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.RL
{
    public class PolicyGradientTests
    {
        [Fact]
        public void Constructor_WithPositiveActionCount_ShouldSucceed()
        {
            var pg = new PolicyGradient(10);

            Assert.NotNull(pg);
        }

        [Fact]
        public void Constructor_WithZeroActionCount_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new PolicyGradient(0));
        }

        [Fact]
        public void Constructor_WithNegativeActionCount_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new PolicyGradient(-1));
        }

        [Fact]
        public void Update_WithValidReward_ShouldNotThrow()
        {
            var pg = new PolicyGradient(10);
            var state = TestDataFactory.GenerateTestVector(0);

            pg.Update(state, 5, 0.8);

            Assert.NotNull(pg);
        }

        [Fact]
        public void Update_WithNullState_ShouldThrow()
        {
            var pg = new PolicyGradient(10);

            Assert.Throws<ArgumentNullException>(() =>
                pg.Update(null!, 5, 0.8));
        }

        [Fact]
        public void Update_WithNegativeReward_ShouldNotThrow()
        {
            var pg = new PolicyGradient(10);
            var state = TestDataFactory.GenerateTestVector(0);

            pg.Update(state, 5, -0.5);

            Assert.NotNull(pg);
        }

        [Fact]
        public void GetPolicy_ShouldReturnProbabilities()
        {
            var pg = new PolicyGradient(10);
            var state = TestDataFactory.GenerateTestVector(0);

            var policy = pg.GetPolicy(state);

            Assert.NotNull(policy);
            Assert.Equal(10, policy.Length);
        }

        [Fact]
        public void GetPolicy_WithNullState_ShouldThrow()
        {
            var pg = new PolicyGradient(10);

            Assert.Throws<ArgumentNullException>(() =>
                pg.GetPolicy(null!));
        }

        [Fact]
        public void GetPolicy_Probabilities_ShouldSumToOne()
        {
            var pg = new PolicyGradient(10);
            var state = TestDataFactory.GenerateTestVector(0);

            var policy = pg.GetPolicy(state);

            var sum = policy.Sum();
            Assert.InRange(sum, 0.99, 1.01);
        }
    }
}
