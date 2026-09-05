
using CodeRLFramework.VectorSpace;
using CodeRLFramework.Tests.Fixtures;
using Xunit;

namespace CodeRLFramework.Tests.UnitTests.VectorSpace
{
    public class GraphSnapshotTests
    {
        [Fact]
        public void Constructor_Default_ShouldInitialize()
        {
            var snapshot = new GraphSnapshot();

            Assert.NotNull(snapshot);
            Assert.NotNull(snapshot.FunctionVectors);
            Assert.Empty(snapshot.FunctionVectors);
            Assert.NotNull(snapshot.SnapshotTime);
        }

        [Fact]
        public void AddFunctionVector_ShouldIncreaseCount()
        {
            var snapshot = new GraphSnapshot();
            var vector = TestDataFactory.GenerateTestVector(0);

            snapshot.FunctionVectors.Add(vector);

            Assert.Single(snapshot.FunctionVectors);
        }

        [Fact]
        public void AddMultipleVectors_ShouldMaintainOrder()
        {
            var snapshot = new GraphSnapshot();

            snapshot.FunctionVectors.Add(TestDataFactory.GenerateTestVector(0));
            snapshot.FunctionVectors.Add(TestDataFactory.GenerateTestVector(1));
            snapshot.FunctionVectors.Add(TestDataFactory.GenerateTestVector(2));

            Assert.Equal(3, snapshot.FunctionVectors.Count);
        }

        [Fact]
        public void Clear_ShouldRemoveAll()
        {
            var snapshot = new GraphSnapshot();
            snapshot.FunctionVectors.Add(TestDataFactory.GenerateTestVector(0));
            snapshot.FunctionVectors.Add(TestDataFactory.GenerateTestVector(1));

            snapshot.FunctionVectors.Clear();

            Assert.Empty(snapshot.FunctionVectors);
        }

        [Fact]
        public void SnapshotTime_ShouldBeRecent()
        {
            var before = DateTime.UtcNow.AddSeconds(-1);

            var snapshot = new GraphSnapshot();

            Assert.True(snapshot.SnapshotTime >= before);
            Assert.True(snapshot.SnapshotTime <= DateTime.UtcNow.AddSeconds(1));
        }
    }
}
