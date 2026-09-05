
using CodeRLFramework.Generation;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Generation
{
    public class CodeTokenizerTests
    {
        [Fact]
        public void Constructor_WithNullFunctionSpace_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CodeTokenizer(null!));
        }

        [Fact]
        public void Constructor_WithValidFunctionSpace_ShouldSucceed()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();

            var tokenizer = new CodeTokenizer(space);

            Assert.NotNull(tokenizer);
        }

        [Fact]
        public void Encode_WithValidFunctionNames_ShouldReturnTokens()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var functionNames = new List<string> { "Initialize", "Process", "Finalize" };

            var tokens = tokenizer.Encode(functionNames);

            Assert.NotNull(tokens);
            Assert.NotEmpty(tokens);
            Assert.Equal(functionNames.Count, tokens.Length);
        }

        [Fact]
        public void Encode_WithEmptyList_ShouldReturnEmpty()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);

            var tokens = tokenizer.Encode(new List<string>());

            Assert.NotNull(tokens);
            Assert.Empty(tokens);
        }

        [Fact]
        public void Encode_WithNull_ShouldThrow()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);

            Assert.Throws<ArgumentNullException>(() =>
                tokenizer.Encode(null!));
        }

        [Fact]
        public void Decode_WithValidTokens_ShouldReturnFunctionNames()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var tokens = tokenizer.Encode(
                new List<string> { "Initialize", "Process", "Finalize" });

            var names = tokenizer.Decode(tokens);

            Assert.NotNull(names);
            Assert.Equal(3, names.Count);
            Assert.Contains("Initialize", names);
            Assert.Contains("Process", names);
            Assert.Contains("Finalize", names);
        }

        [Fact]
        public void Decode_WithEmptyArray_ShouldReturnEmpty()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);

            var names = tokenizer.Decode(Array.Empty<int>());

            Assert.NotNull(names);
            Assert.Empty(names);
        }

        [Fact]
        public void EncodeDecode_RoundTrip_ShouldPreserveNames()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var originalNames = new List<string> { "Initialize", "Process", "Finalize" };

            var tokens = tokenizer.Encode(originalNames);
            var decodedNames = tokenizer.Decode(tokens);

            Assert.Equal(originalNames, decodedNames);
        }

        [Fact]
        public void Encode_WithUnknownFunction_ShouldAssignDefaultToken()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var functionNames = new List<string> { "NonExistentFunction" };

            var tokens = tokenizer.Encode(functionNames);

            Assert.NotNull(tokens);
            Assert.Single(tokens);
        }
    }
}
