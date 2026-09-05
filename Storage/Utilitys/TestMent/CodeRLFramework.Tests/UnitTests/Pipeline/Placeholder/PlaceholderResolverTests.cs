
using CodeRLFramework.Pipeline.Placeholder;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Pipeline.Placeholder
{
    public class PlaceholderResolverTests
    {
        [Fact]
        public void Resolve_WithInputPlaceholder_ShouldResolve()
        {
            var resolver = new PlaceholderResolver();
            var code = "@input:template";
            var context = TestDataFactory.CreateBasicContext();

            var result = resolver.Resolve(code, context);

            Assert.NotNull(result);
            Assert.DoesNotContain("@input:", result);
        }

        [Fact]
        public void Resolve_WithNodePlaceholder_ShouldResolve()
        {
            var resolver = new PlaceholderResolver();
            var code = "@node:Initialize";
            var context = TestDataFactory.CreateBasicContext();

            var result = resolver.Resolve(code, context);

            Assert.NotNull(result);
            Assert.DoesNotContain("@node:", result);
        }

        [Fact]
        public void Resolve_WithPrevStepPlaceholder_ShouldResolve()
        {
            var resolver = new PlaceholderResolver();
            var code = "@prevstep";
            var context = TestDataFactory.CreateBasicContext();

            var result = resolver.Resolve(code, context);

            Assert.NotNull(result);
            Assert.DoesNotContain("@prevstep", result);
        }

        [Fact]
        public void Resolve_WithNullCode_ShouldThrow()
        {
            var resolver = new PlaceholderResolver();

            Assert.Throws<ArgumentNullException>(() =>
                resolver.Resolve(null!, TestDataFactory.CreateBasicContext()));
        }

        [Fact]
        public void Resolve_WithNullContext_ShouldThrow()
        {
            var resolver = new PlaceholderResolver();

            Assert.Throws<ArgumentNullException>(() =>
                resolver.Resolve("test code", null!));
        }

        [Fact]
        public void Resolve_WithoutPlaceholders_ShouldReturnOriginal()
        {
            var resolver = new PlaceholderResolver();
            var code = "public void Test() { }";

            var result = resolver.Resolve(code, TestDataFactory.CreateBasicContext());

            Assert.Equal(code, result);
        }

        [Fact]
        public void Resolve_WithMultiplePlaceholders_ShouldResolveAll()
        {
            var resolver = new PlaceholderResolver();
            var code = "@input:template @node:Initialize @prevstep";
            var context = TestDataFactory.CreateBasicContext();

            var result = resolver.Resolve(code, context);

            Assert.NotNull(result);
            Assert.DoesNotContain("@", result);
        }

        [Fact]
        public void Resolve_WithEmptyCode_ShouldReturnEmpty()
        {
            var resolver = new PlaceholderResolver();

            var result = resolver.Resolve("", TestDataFactory.CreateBasicContext());

            Assert.Equal("", result);
        }

        [Fact]
        public void Resolve_WithUnknownPlaceholder_ShouldKeepOriginal()
        {
            var resolver = new PlaceholderResolver();
            var code = "@unknown:placeholder";
            var context = TestDataFactory.CreateBasicContext();

            var result = resolver.Resolve(code, context);

            Assert.NotNull(result);
            Assert.Contains("@unknown:placeholder", result);
        }
    }
}
