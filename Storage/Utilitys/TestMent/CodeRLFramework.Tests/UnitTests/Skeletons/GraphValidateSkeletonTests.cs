
using CodeRLFramework.Pipeline.Skeletons;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Skeletons
{
    public class GraphValidateSkeletonTests
    {
        [Fact]
        public void Execute_WithValidGraphSnapshot_ShouldSucceed()
        {
            var skeleton = new GraphValidateSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.GraphSnapshot);
        }

        [Fact]
        public void Execute_WithoutGraphSnapshot_ShouldThrow()
        {
            var skeleton = new GraphValidateSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = null;
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithNullContext_ShouldThrow()
        {
            var skeleton = new GraphValidateSkeleton();

            Assert.Throws<ArgumentNullException>(() =>
                skeleton.Execute(null!, TestDataFactory.CreateSkeletonContext()));
        }

        [Fact]
        public void Execute_WithEmptyGraphSnapshot_ShouldThrow()
        {
            var skeleton = new GraphValidateSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = TestDataFactory.CreateBasicGraphSnapshot();
            context.GraphSnapshot.Nodes.Clear();
            context.GraphSnapshot.Edges.Clear();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            Assert.Throws<InvalidOperationException>(() =>
                skeleton.Execute(context, skelContext));
        }

        [Fact]
        public void Execute_WithComplexGraphSnapshot_ShouldSucceed()
        {
            var skeleton = new GraphValidateSkeleton();
            var context = TestDataFactory.CreateBasicContext();
            context.GraphSnapshot = TestDataFactory.CreateComplexGraphSnapshot();
            using var skelContext = TestDataFactory.CreateSkeletonContext();

            var result = skeleton.Execute(context, skelContext);

            Assert.NotNull(result);
            Assert.NotNull(result.GraphSnapshot);
            Assert.Equal(12, result.GraphSnapshot.NodeCount);
        }
    }
}
