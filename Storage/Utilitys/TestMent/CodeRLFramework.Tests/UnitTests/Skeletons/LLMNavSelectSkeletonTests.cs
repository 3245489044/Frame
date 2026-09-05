
using CodeRLFramework.Generation;
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using CodeRLFramework.VectorSpace;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class LLMNavSelectSkeletonTests
    {
        private readonly FunctionVectorSpace _functionSpace;
        private readonly CodeTokenizer _tokenizer;
        private readonly CodeGenerationAgent _agent;

        public LLMNavSelectSkeletonTests()
        {
            _functionSpace = TestDataFactory.CreateBasicFunctionSpace();
            _tokenizer = new CodeTokenizer(_functionSpace);
            _agent = new CodeGenerationAgent(_tokenizer);
        }

        [Fact]
        public void Execute_WithValidContext_ShouldNavigate()
        {
            var skeleton = new LLMNavSelectSkeleton(_agent);
            var context = TestDataFactory.CreateBasicContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new LLMNavSelectSkeleton(_agent);

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithoutGraphSnapshot_ShouldThrow()
        {
            var skeleton = new LLMNavSelectSkeleton(_agent);
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithoutFunctionSpace_ShouldThrow()
        {
            var skeleton = new LLMNavSelectSkeleton(_agent);
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithEmptyFunctionSpace_ShouldHandleGracefully()
        {
            var skeleton = new LLMNavSelectSkeleton(_agent);
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
        }

        [Fact]
        public void Constructor_WithNullAgent_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LLMNavSelectSkeleton(null!));
        }

        [Fact]
        public void Constructor_WithValidAgent_ShouldSucceed()
        {
            var skeleton = new LLMNavSelectSkeleton(_agent);

            Assert.NotNull(skeleton);
        }
    }
}
