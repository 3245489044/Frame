
using CodeRLFramework.Generation;
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using CodeRLFramework.VectorSpace;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class CodeGenSkeletonTests
    {
        private readonly FunctionVectorSpace _functionSpace;
        private readonly CodeTokenizer _tokenizer;
        private readonly CodeGenerationAgent _agent;
        private readonly LLMNavSelectSkeleton _navSkeleton;

        public CodeGenSkeletonTests()
        {
            _functionSpace = TestDataFactory.CreateBasicFunctionSpace();
            _tokenizer = new CodeTokenizer(_functionSpace);
            _agent = new CodeGenerationAgent(_tokenizer);
            _navSkeleton = new LLMNavSelectSkeleton(_agent);
        }

        [Fact]
        public void Execute_WithValidContext_ShouldGenerateCode()
        {
            var skeleton = new CodeGenSkeleton(_agent, _navSkeleton);
            var context = TestDataFactory.CreateContextWithTokens();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.GeneratedCode);
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new CodeGenSkeleton(_agent, _navSkeleton);

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithoutGeneratedTokens_ShouldThrow()
        {
            var skeleton = new CodeGenSkeleton(_agent, _navSkeleton);
            var context = TestDataFactory.CreateBasicContext();
            context.GeneratedTokens = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithoutGraphSnapshot_ShouldThrow()
        {
            var skeleton = new CodeGenSkeleton(_agent, _navSkeleton);
            var context = TestDataFactory.CreateContextWithTokens();
            context.GraphSnapshot = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Constructor_WithDefaultParams_ShouldSucceed()
        {
            var skeleton = new CodeGenSkeleton();

            Assert.NotNull(skeleton);
        }

        [Fact]
        public void Constructor_WithAgentOnly_ShouldSucceed()
        {
            var skeleton = new CodeGenSkeleton(_agent);

            Assert.NotNull(skeleton);
        }

        [Fact]
        public void Constructor_WithBothParams_ShouldSucceed()
        {
            var skeleton = new CodeGenSkeleton(_agent, _navSkeleton);

            Assert.NotNull(skeleton);
        }
    }
}
