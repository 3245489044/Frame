
using CodeRLFramework.Pipeline;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Pipeline
{
    public class CodeRLAdapterSkeletonTests
    {
        [Fact]
        public void Execute_WithValidContext_ShouldAdapt()
        {
            var skeleton = new CodeRLAdapterSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new CodeRLAdapterSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithFunctionSpace_ShouldAdapt()
        {
            var skeleton = new CodeRLAdapterSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateComplexFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithEmptyFunctionSpace_ShouldNotThrow()
        {
            var skeleton = new CodeRLAdapterSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }
    }
}
