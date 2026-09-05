
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class ConvertSkeletonTests
    {
        [Fact]
        public void Execute_WithValidContext_ShouldConvert()
        {
            var skeleton = new ConvertSkeleton();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new ConvertSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithoutGeneratedCode_ShouldNotThrow()
        {
            var skeleton = new ConvertSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.GeneratedCode = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Constructor_WithDefaultConverter_ShouldSucceed()
        {
            var skeleton = new ConvertSkeleton();

            Assert.NotNull(skeleton);
        }
    }
}
