
using CodeRLFramework.Pipeline.Models;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Pipeline.Models
{
    public class GraphNavigationContextTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var context = new GraphNavigationContext();

            Assert.NotNull(context);
            Assert.NotNull(context.VisitedNodes);
            Assert.Empty(context.VisitedNodes);
            Assert.Equal(0, context.FallbackCount);
            Assert.Equal(0, context.NavigationDepth);
        }

        [Fact]
        public void AddVisitedNode_ShouldAddToList()
        {
            var context = new GraphNavigationContext();

            context.AddVisitedNode("Initialize");
            context.AddVisitedNode("Process");

            Assert.Equal(2, context.VisitedNodes.Count);
            Assert.Contains("Initialize", context.VisitedNodes);
            Assert.Contains("Process", context.VisitedNodes);
        }

        [Fact]
        public void AddVisitedNode_WithNull_ShouldThrow()
        {
            var context = new GraphNavigationContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.AddVisitedNode(null!));
        }

        [Fact]
        public void AddVisitedNode_WithEmpty_ShouldThrow()
        {
            var context = new GraphNavigationContext();

            Assert.Throws<ArgumentException>(() =>
                context.AddVisitedNode(""));
        }

        [Fact]
        public void IncrementFallback_ShouldIncreaseCount()
        {
            var context = new GraphNavigationContext();

            context.IncrementFallback();
            context.IncrementFallback();

            Assert.Equal(2, context.FallbackCount);
        }

        [Fact]
        public void IncrementDepth_ShouldIncreaseDepth()
        {
            var context = new GraphNavigationContext();

            context.IncrementDepth();
            context.IncrementDepth();
            context.IncrementDepth();

            Assert.Equal(3, context.NavigationDepth);
        }

        [Fact]
        public void Reset_ShouldClearState()
        {
            var context = TestDataFactory.CreateBasicNavContext();
            context.AddVisitedNode("Initialize");
            context.IncrementFallback();
            context.IncrementDepth();

            context.Reset();

            Assert.Empty(context.VisitedNodes);
            Assert.Equal(0, context.FallbackCount);
            Assert.Equal(0, context.NavigationDepth);
        }

        [Fact]
        public void HasVisited_WithVisitedNode_ShouldReturnTrue()
        {
            var context = new GraphNavigationContext();
            context.AddVisitedNode("Initialize");

            var hasVisited = context.HasVisited("Initialize");

            Assert.True(hasVisited);
        }

        [Fact]
        public void HasVisited_WithUnvisitedNode_ShouldReturnFalse()
        {
            var context = new GraphNavigationContext();
            context.AddVisitedNode("Initialize");

            var hasVisited = context.HasVisited("Process");

            Assert.False(hasVisited);
        }

        [Fact]
        public void HasVisited_WithNull_ShouldThrow()
        {
            var context = new GraphNavigationContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.HasVisited(null!));
        }

        [Fact]
        public void CurrentNode_ShouldReturnLastVisited()
        {
            var context = new GraphNavigationContext();
            context.AddVisitedNode("Initialize");
            context.AddVisitedNode("Process");

            var currentNode = context.CurrentNode;

            Assert.Equal("Process", currentNode);
        }

        [Fact]
        public void CurrentNode_WithEmptyVisited_ShouldReturnNull()
        {
            var context = new GraphNavigationContext();

            var currentNode = context.CurrentNode;

            Assert.Null(currentNode);
        }
    }
}
