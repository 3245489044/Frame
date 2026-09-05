
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class PlaceholderResolveSkeletonTests
    {
        [Fact]
        public void Execute_WithValidContext_ShouldResolvePlaceholders()
        {
            var skeleton = new PlaceholderResolveSkeleton();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = TestDataFactory.SampleCodeSnippets.WithPlaceholder;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.DoesNotContain("@input:", result.GeneratedCode);
            Assert.DoesNotContain("@node:", result.GeneratedCode);
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new PlaceholderResolveSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithoutPlaceholders_ShouldNotModify()
        {
            var skeleton = new PlaceholderResolveSkeleton();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            var originalCode = context.GeneratedCode;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.Equal(originalCode, result.GeneratedCode);
        }

        [Fact]
        public void Execute_WithNullGeneratedCode_ShouldNotThrow()
        {
            var skeleton = new PlaceholderResolveSkeleton();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithEmptyGeneratedCode_ShouldNotThrow()
        {
            var skeleton = new PlaceholderResolveSkeleton();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = "";
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.Equal("", result.GeneratedCode);
        }

        [Fact]
        public void Execute_WithMultiplePlaceholders_ShouldResolveAll()
        {
            var skeleton = new PlaceholderResolveSkeleton();
            var context = TestDataFactory.CreateContextWithGeneratedCode();
            context.GeneratedCode = "@input:template @node:Initialize @prevstep @node:Process";
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
            Assert.DoesNotContain("@", result.GeneratedCode);
        }
    }
}
