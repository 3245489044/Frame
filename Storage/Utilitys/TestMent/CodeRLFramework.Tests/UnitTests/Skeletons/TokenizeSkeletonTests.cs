
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class TokenizeSkeletonTests
    {
        [Fact]
        public void Execute_WithValidContext_ShouldEncodeTokens()
        {
            var skeleton = new TokenizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.Tokenizer);
            Assert.NotNull(result.GeneratedTokens);
            Assert.NotEmpty(result.GeneratedTokens);
        }

        [Fact]
        public void Execute_WithNullFunctionSpace_ShouldThrow()
        {
            var skeleton = new TokenizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new TokenizeSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithEmptyFunctionSpace_ShouldNotThrow()
        {
            var skeleton = new TokenizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.Tokenizer);
        }

        [Fact]
        public void Execute_WithComplexFunctionSpace_ShouldEncodeAllTokens()
        {
            var skeleton = new TokenizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateComplexFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedTokens);
            Assert.NotEmpty(result.GeneratedTokens);
        }
    }
}
