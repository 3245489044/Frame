
using CodeRLFramework.Pipeline.Placeholder;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Pipeline.Placeholder
{
    public class PlaceholderTypesTests
    {
        [Fact]
        public void InputPlaceholder_ShouldHaveCorrectPrefix()
        {
            var placeholder = new InputPlaceholder("template");

            Assert.Equal("@input:", placeholder.Prefix);
            Assert.Equal("template", placeholder.Key);
        }

        [Fact]
        public void InputPlaceholder_WithNullKey_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new InputPlaceholder(null!));
        }

        [Fact]
        public void InputPlaceholder_WithEmptyKey_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new InputPlaceholder(""));
        }

        [Fact]
        public void NodePlaceholder_ShouldHaveCorrectPrefix()
        {
            var placeholder = new NodePlaceholder("Initialize");

            Assert.Equal("@node:", placeholder.Prefix);
            Assert.Equal("Initialize", placeholder.Key);
        }

        [Fact]
        public void NodePlaceholder_WithNullKey_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NodePlaceholder(null!));
        }

        [Fact]
        public void PrevStepPlaceholder_ShouldHaveCorrectPrefix()
        {
            var placeholder = new PrevStepPlaceholder();

            Assert.Equal("@prevstep", placeholder.Prefix);
            Assert.Equal("prevstep", placeholder.Key);
        }

        [Fact]
        public void PlaceholderFactory_CreateInput_ShouldReturnCorrectType()
        {
            var placeholder = PlaceholderFactory.Create("@input:template");

            Assert.NotNull(placeholder);
            Assert.IsType<InputPlaceholder>(placeholder);
        }

        [Fact]
        public void PlaceholderFactory_CreateNode_ShouldReturnCorrectType()
        {
            var placeholder = PlaceholderFactory.Create("@node:Initialize");

            Assert.NotNull(placeholder);
            Assert.IsType<NodePlaceholder>(placeholder);
        }

        [Fact]
        public void PlaceholderFactory_CreatePrevStep_ShouldReturnCorrectType()
        {
            var placeholder = PlaceholderFactory.Create("@prevstep");

            Assert.NotNull(placeholder);
            Assert.IsType<PrevStepPlaceholder>(placeholder);
        }

        [Fact]
        public void PlaceholderFactory_CreateUnknown_ShouldReturnNull()
        {
            var placeholder = PlaceholderFactory.Create("@unknown:test");

            Assert.Null(placeholder);
        }

        [Fact]
        public void PlaceholderFactory_CreateNull_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                PlaceholderFactory.Create(null!));
        }

        [Fact]
        public void PlaceholderFactory_CreateEmpty_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                PlaceholderFactory.Create(""));
        }
    }
}
