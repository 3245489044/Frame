
using CodeRLFramework.Output;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.Output
{
    public class OutputVectorSpaceTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var space = new OutputVectorSpace();

            Assert.NotNull(space);
            Assert.NotNull(space.FunctionNames);
            Assert.Empty(space.FunctionNames);
            Assert.NotNull(space.Vectors);
            Assert.Empty(space.Vectors);
        }

        [Fact]
        public void AddFunction_ShouldIncreaseCount()
        {
            var space = new OutputVectorSpace();

            space.FunctionNames.Add("Initialize");
            space.Vectors.Add(TestDataFactory.GenerateTestVector(0));

            Assert.Single(space.FunctionNames);
            Assert.Single(space.Vectors);
        }

        [Fact]
        public void AddMultipleFunctions_ShouldMaintainOrder()
        {
            var space = new OutputVectorSpace();

            space.FunctionNames.Add("Initialize");
            space.FunctionNames.Add("Process");
            space.FunctionNames.Add("Finalize");

            Assert.Equal(3, space.FunctionNames.Count);
            Assert.Equal("Initialize", space.FunctionNames[0]);
            Assert.Equal("Process", space.FunctionNames[1]);
            Assert.Equal("Finalize", space.FunctionNames[2]);
        }

        [Fact]
        public void Clear_ShouldRemoveAll()
        {
            var space = new OutputVectorSpace();
            space.FunctionNames.Add("Initialize");
            space.Vectors.Add(TestDataFactory.GenerateTestVector(0));

            space.FunctionNames.Clear();
            space.Vectors.Clear();

            Assert.Empty(space.FunctionNames);
            Assert.Empty(space.Vectors);
        }

        [Fact]
        public void FunctionCount_ShouldMatchVectorsCount()
        {
            var space = new OutputVectorSpace();

            space.FunctionNames.AddRange(new[] { "A", "B", "C" });
            space.Vectors.AddRange(new[]
            {
                TestDataFactory.GenerateTestVector(0),
                TestDataFactory.GenerateTestVector(1),
                TestDataFactory.GenerateTestVector(2)
            });

            Assert.Equal(space.FunctionNames.Count, space.Vectors.Count);
        }
    }
}
