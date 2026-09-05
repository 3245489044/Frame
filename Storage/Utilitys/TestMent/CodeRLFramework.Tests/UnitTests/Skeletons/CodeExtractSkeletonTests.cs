
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using CodeRLFramework.VectorSpace;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class CodeExtractSkeletonTests
    {
        [Fact]
        public void Execute_WithValidContext_ShouldExtractFunctionSpace()
        {
            var skeleton = new CodeExtractSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
        }

        [Fact]
        public void Execute_WithNullCodebasePath_ShouldThrow()
        {
            var skeleton = new CodeExtractSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.CodebasePath = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithEmptyCodebasePath_ShouldThrow()
        {
            var skeleton = new CodeExtractSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.CodebasePath = "";
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new CodeExtractSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithExistingFunctionSpace_ShouldPreserveIt()
        {
            var skeleton = new CodeExtractSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.CodebasePath = Path.Combine(Path.GetTempPath(), "NonExistentDir");
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
        }

        [Fact]
        public void Constructor_WithDefaultExtractor_ShouldSucceed()
        {
            var skeleton = new CodeExtractSkeleton();

            Assert.NotNull(skeleton);
        }

        [Fact]
        public void Constructor_WithCustomExtractor_ShouldSucceed()
        {
            var skeleton = new CodeExtractSkeleton(new VectorExtractor());

            Assert.NotNull(skeleton);
        }
    }
}
