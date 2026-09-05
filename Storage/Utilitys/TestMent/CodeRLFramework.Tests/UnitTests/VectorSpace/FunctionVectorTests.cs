
using CodeRLFramework.VectorSpace;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.VectorSpace
{
    public class FunctionVectorTests
    {
        [Fact]
        public void Constructor_WithValidParams_ShouldInitialize()
        {
            var name = "Initialize";
            var vector = TestDataFactory.GenerateTestVector(0);

            var funcVector = new FunctionVector(name, vector);

            Assert.NotNull(funcVector);
            Assert.Equal(name, funcVector.FunctionName);
            Assert.Equal(vector, funcVector.Vector);
        }

        [Fact]
        public void Constructor_WithNullName_ShouldThrow()
        {
            var vector = TestDataFactory.GenerateTestVector(0);

            Assert.Throws<ArgumentNullException>(() =>
                new FunctionVector(null!, vector));
        }

        [Fact]
        public void Constructor_WithEmptyName_ShouldThrow()
        {
            var vector = TestDataFactory.GenerateTestVector(0);

            Assert.Throws<ArgumentException>(() =>
                new FunctionVector("", vector));
        }

        [Fact]
        public void Constructor_WithNullVector_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FunctionVector("Initialize", null!));
        }

        [Fact]
        public void Constructor_WithEmptyVector_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new FunctionVector("Initialize", Array.Empty<double>()));
        }

        [Fact]
        public void Dimension_ShouldMatchVectorLength()
        {
            var vector = new double[] { 0.1, 0.2, 0.3, 0.4, 0.5 };
            var funcVector = new FunctionVector("Test", vector);

            Assert.Equal(5, funcVector.Dimension);
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            var vector = new double[] { 0.1, 0.2, 0.3 };
            var funcVector = new FunctionVector("Test", vector);

            var str = funcVector.ToString();

            Assert.Contains("Test", str);
            Assert.Contains("3", str);
        }

        [Fact]
        public void Equals_SameValues_ShouldReturnTrue()
        {
            var vector1 = new double[] { 0.1, 0.2, 0.3 };
            var vector2 = new double[] { 0.1, 0.2, 0.3 };
            var funcVector1 = new FunctionVector("Test", vector1);
            var funcVector2 = new FunctionVector("Test", vector2);

            var areEqual = funcVector1.Equals(funcVector2);

            Assert.True(areEqual);
        }

        [Fact]
        public void Equals_DifferentValues_ShouldReturnFalse()
        {
            var vector1 = new double[] { 0.1, 0.2, 0.3 };
            var vector2 = new double[] { 0.4, 0.5, 0.6 };
            var funcVector1 = new FunctionVector("Test", vector1);
            var funcVector2 = new FunctionVector("Test", vector2);

            var areEqual = funcVector1.Equals(funcVector2);

            Assert.False(areEqual);
        }

        [Fact]
        public void GetHashCode_SameValues_ShouldBeEqual()
        {
            var vector1 = new double[] { 0.1, 0.2, 0.3 };
            var vector2 = new double[] { 0.1, 0.2, 0.3 };
            var funcVector1 = new FunctionVector("Test", vector1);
            var funcVector2 = new FunctionVector("Test", vector2);

            Assert.Equal(funcVector1.GetHashCode(), funcVector2.GetHashCode());
        }
    }
}
