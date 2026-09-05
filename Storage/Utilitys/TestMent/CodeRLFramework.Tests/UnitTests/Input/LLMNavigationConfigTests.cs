
using CodeRLFramework.Input;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Input
{
    public class LLMNavigationConfigTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var config = new LLMNavigationConfig();

            Assert.NotNull(config);
            Assert.False(config.Enabled);
            Assert.Equal(5, config.MaxDepth);
            Assert.Equal(3, config.MaxFallbackCount);
        }

        [Fact]
        public void CustomValues_ShouldBeSet()
        {
            var config = new LLMNavigationConfig
            {
                Enabled = true,
                MaxDepth = 10,
                MaxFallbackCount = 5
            };

            Assert.True(config.Enabled);
            Assert.Equal(10, config.MaxDepth);
            Assert.Equal(5, config.MaxFallbackCount);
        }

        [Fact]
        public void MaxDepth_ShouldBePositive()
        {
            var config = new LLMNavigationConfig();

            Assert.True(config.MaxDepth > 0);
        }

        [Fact]
        public void MaxFallbackCount_ShouldBePositive()
        {
            var config = new LLMNavigationConfig();

            Assert.True(config.MaxFallbackCount > 0);
        }
    }
}
