
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using CodeRLFramework.VectorSpace;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class VectorizeSkeletonTests
    {
        [Fact]
        public void Execute_WithValidContext_ShouldAttachVectors()
        {
            var skeleton = new VectorizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
            Assert.All(result.FunctionSpace.Functions, f =>
            {
                Assert.NotNull(f.SemanticVector);
                Assert.NotEmpty(f.SemanticVector);
                Assert.NotNull(f.StructuralVector);
                Assert.NotEmpty(f.StructuralVector);
                Assert.NotNull(f.RelationVector);
                Assert.NotEmpty(f.RelationVector);
            });
        }

        [Fact]
        public void Execute_WithNullFunctionSpace_ShouldThrow()
        {
            var skeleton = new VectorizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new VectorizeSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithEmptyFunctionSpace_ShouldNotThrow()
        {
            var skeleton = new VectorizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateEmptyFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
            Assert.Empty(result.FunctionSpace.Functions);
        }

        [Fact]
        public void Execute_WithComplexFunctionSpace_ShouldVectorizeAll()
        {
            var skeleton = new VectorizeSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.FunctionSpace = TestDataFactory.CreateComplexFunctionSpace();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionSpace);
            Assert.Equal(12, result.FunctionSpace.Functions.Count);
            Assert.All(result.FunctionSpace.Functions, f =>
            {
                Assert.NotNull(f.SemanticVector);
                Assert.NotNull(f.StructuralVector);
                Assert.NotNull(f.RelationVector);
            });
        }

        [Fact]
        public void Constructor_WithDefaultExtractor_ShouldSucceed()
        {
            var skeleton = new VectorizeSkeleton();

            Assert.NotNull(skeleton);
        }

        [Fact]
        public void Constructor_WithCustomExtractor_ShouldSucceed()
        {
            var skeleton = new VectorizeSkeleton(new VectorExtractor());

            Assert.NotNull(skeleton);
        }
    }
}
