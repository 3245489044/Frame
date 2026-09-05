
using CodeRLFramework.Tests.Fixtures;
using CodeRLFramework.VectorSpace;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.VectorSpace
{
    public class FunctionVectorSpaceTests
    {
        [Fact]
        public void Constructor_Default_ShouldCreateEmpty()
        {
            var space = new FunctionVectorSpace();

            Assert.NotNull(space);
            Assert.NotNull(space.Functions);
            Assert.Empty(space.Functions);
        }

        [Fact]
        public void AddFunction_ShouldIncreaseCount()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();

            Assert.Equal(3, space.Functions.Count);
        }

        [Fact]
        public void FindFunctionByName_Existing_ShouldReturn()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();

            var func = space.Functions.FirstOrDefault(f => f.Name == "Process");

            Assert.NotNull(func);
            Assert.Equal("Process", func.Name);
            Assert.Equal("int", func.ReturnType);
        }

        [Fact]
        public void FindFunctionByName_NonExisting_ShouldReturnNull()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();

            var func = space.Functions.FirstOrDefault(f => f.Name == "NonExistent");

            Assert.Null(func);
        }

        [Fact]
        public void ComplexSpace_ShouldHave12Functions()
        {
            var space = TestDataFactory.CreateComplexFunctionSpace();

            Assert.Equal(12, space.Functions.Count);
        }

        [Fact]
        public void EmptySpace_ShouldHaveNoFunctions()
        {
            var space = TestDataFactory.CreateEmptyFunctionSpace();

            Assert.Empty(space.Functions);
        }

        [Fact]
        public void FunctionVector_ShouldHaveVectors()
        {
            var space = TestDataFactory.CreateBasicFunctionSpace();

            foreach (var func in space.Functions)
            {
                Assert.NotNull(func.SemanticVector);
                Assert.NotEmpty(func.SemanticVector);
                Assert.NotNull(func.StructuralVector);
                Assert.NotEmpty(func.StructuralVector);
                Assert.NotNull(func.RelationVector);
                Assert.NotEmpty(func.RelationVector);
            }
        }
    }
}
