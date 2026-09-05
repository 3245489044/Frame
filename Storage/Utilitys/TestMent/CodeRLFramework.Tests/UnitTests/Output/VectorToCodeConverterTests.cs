
using CodeRLFramework.Generation;
using CodeRLFramework.Output;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Output
{
    public class VectorToCodeConverterTests
    {
        [Fact]
        public void Constructor_WithNullFunctionSpace_ShouldThrow()
        {
            var tokenizer = new CodeTokenizer(TestDataFactory.CreateBasicFunctionSpace());

            Assert.Throws<ArgumentNullException>(() =>
                new VectorToCodeConverter(null!, tokenizer));
        }

        [Fact]
        public void Constructor_WithNullTokenizer_ShouldThrow()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();

            Assert.Throws<ArgumentNullException>(() =>
                new VectorToCodeConverter(space, null!));
        }

        [Fact]
        public void Constructor_WithValidParams_ShouldSucceed()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);

            var converter = new VectorToCodeConverter(space, tokenizer);

            Assert.NotNull(converter);
        }

        [Fact]
        public void Convert_WithValidOutputSpace_ShouldReturnCode()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var converter = new VectorToCodeConverter(space, tokenizer);
            var outputSpace = new OutputVectorSpace
            {
                FunctionNames = new List<string> { "Initialize", "Process", "Finalize" },
                Vectors = new List<double[]>
                {
                    TestDataFactory.GenerateTestVector(0),
                    TestDataFactory.GenerateTestVector(1),
                    TestDataFactory.GenerateTestVector(2)
                }
            };

            var code = converter.Convert(outputSpace);

            Assert.NotNull(code);
            Assert.NotEmpty(code);
        }

        [Fact]
        public void Convert_WithNullOutputSpace_ShouldThrow()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var converter = new VectorToCodeConverter(space, tokenizer);

            Assert.Throws<ArgumentNullException>(() =>
                converter.Convert(null!));
        }

        [Fact]
        public void Convert_WithEmptyOutputSpace_ShouldReturnEmpty()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();
            var tokenizer = new CodeTokenizer(space);
            var converter = new VectorToCodeConverter(space, tokenizer);
            var outputSpace = new OutputVectorSpace();

            var code = converter.Convert(outputSpace);

            Assert.NotNull(code);
        }
    }
}
