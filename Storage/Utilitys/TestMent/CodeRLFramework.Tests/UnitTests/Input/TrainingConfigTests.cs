
using CodeRLFramework.Input;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Input
{
    public class TrainingConfigTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var config = new TrainingConfig();

            Assert.NotNull(config);
            Assert.Equal(100, config.MaxEpisodes);
            Assert.Equal(50, config.MaxStepsPerEpisode);
            Assert.Equal(0.01, config.ConvergenceThreshold);
            Assert.Equal(10, config.ConvergenceWindow);
        }

        [Fact]
        public void CustomValues_ShouldBeSet()
        {
            var config = new TrainingConfig
            {
                MaxEpisodes = 50,
                MaxStepsPerEpisode = 25,
                ConvergenceThreshold = 0.005,
                ConvergenceWindow = 5
            };

            Assert.Equal(50, config.MaxEpisodes);
            Assert.Equal(25, config.MaxStepsPerEpisode);
            Assert.Equal(0.005, config.ConvergenceThreshold);
            Assert.Equal(5, config.ConvergenceWindow);
        }

        [Fact]
        public void MaxEpisodes_ShouldBePositive()
        {
            var config = new TrainingConfig();

            Assert.True(config.MaxEpisodes > 0);
        }

        [Fact]
        public void MaxStepsPerEpisode_ShouldBePositive()
        {
            var config = new TrainingConfig();

            Assert.True(config.MaxStepsPerEpisode > 0);
        }

        [Fact]
        public void ConvergenceThreshold_ShouldBePositive()
        {
            var config = new TrainingConfig();

            Assert.True(config.ConvergenceThreshold > 0);
        }

        [Fact]
        public void ConvergenceWindow_ShouldBePositive()
        {
            var config = new TrainingConfig();

            Assert.True(config.ConvergenceWindow > 0);
        }
    }
}
