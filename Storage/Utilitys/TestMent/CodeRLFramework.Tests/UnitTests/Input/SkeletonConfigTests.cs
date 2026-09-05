
using CodeRLFramework.Input;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Input
{
    public class SkeletonConfigTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var config = new SkeletonConfig();

            Assert.NotNull(config);
            Assert.NotNull(config.Disabled);
            Assert.Empty(config.Disabled);
        }

        [Fact]
        public void IsEnabled_WithEmptyDisabled_ShouldReturnTrue()
        {
            var config = new SkeletonConfig();

            var isEnabled = config.IsEnabled("CodeExtract");

            Assert.True(isEnabled);
        }

        [Fact]
        public void IsEnabled_WithDisabledSkeleton_ShouldReturnFalse()
        {
            var config = new SkeletonConfig();
            config.Disabled.Add("CodeExtract");

            var isEnabled = config.IsEnabled("CodeExtract");

            Assert.False(isEnabled);
        }

        [Fact]
        public void IsEnabled_WithMultipleDisabled_ShouldReturnFalse()
        {
            var config = new SkeletonConfig();
            config.Disabled.AddRange(new[] { "CodeExtract", "Tokenize", "Vectorize" });

            Assert.False(config.IsEnabled("CodeExtract"));
            Assert.False(config.IsEnabled("Tokenize"));
            Assert.False(config.IsEnabled("Vectorize"));
            Assert.True(config.IsEnabled("CodeGen"));
        }

        [Fact]
        public void IsEnabled_WithNullName_ShouldThrow()
        {
            var config = new SkeletonConfig();

            Assert.Throws<ArgumentNullException>(() =>
                config.IsEnabled(null!));
        }

        [Fact]
        public void IsEnabled_WithEmptyName_ShouldThrow()
        {
            var config = new SkeletonConfig();

            Assert.Throws<ArgumentException>(() =>
                config.IsEnabled(""));
        }
    }
}
