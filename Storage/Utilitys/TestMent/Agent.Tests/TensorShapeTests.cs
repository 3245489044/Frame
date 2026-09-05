namespace Agent.Tests
{
    public class TensorShapeTests
    {
        [Fact]
        public void TestConstructor()
        {
            var shape = new TensorShape3D(2, 3, 4);
            Assert.Equal(2, shape.Batch);
            Assert.Equal(3, shape.SeqLen);
            Assert.Equal(4, shape.Dim);
        }

        [Fact]
        public void TestEquality()
        {
            var s1 = new TensorShape3D(2, 3, 4);
            var s2 = new TensorShape3D(2, 3, 4);
            var s3 = new TensorShape3D(2, 3, 5);

            Assert.Equal(s1, s2);
            Assert.NotEqual(s1, s3);
            Assert.True(s1 == s2);
            Assert.True(s1 != s3);
        }

        [Fact]
        public void TestToString()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var str = shape.ToString();
            Assert.Contains("2", str);
            Assert.Contains("3", str);
            Assert.Contains("4", str);
        }

        [Fact]
        public void TestTotalElements()
        {
            var shape = new TensorShape3D(2, 3, 4);
            Assert.Equal(24, shape.TotalElements);
        }

        [Fact]
        public void TestEstimatedMemoryBytes()
        {
            var shape = new TensorShape3D(2, 3, 4);
            Assert.Equal(24 * 8, shape.EstimatedMemoryBytes);
        }

        [Fact]
        public void TestValidateIndex()
        {
            var shape = new TensorShape3D(2, 3, 4);
            shape.ValidateIndex(0, 0, 0);
            shape.ValidateIndex(1, 2, 3);

            Assert.Throws<IndexOutOfRangeException>(() => shape.ValidateIndex(2, 0, 0));
            Assert.Throws<IndexOutOfRangeException>(() => shape.ValidateIndex(0, 3, 0));
            Assert.Throws<IndexOutOfRangeException>(() => shape.ValidateIndex(0, 0, 4));
            Assert.Throws<IndexOutOfRangeException>(() => shape.ValidateIndex(-1, 0, 0));
        }

        [Fact]
        public void TestCanBroadcast()
        {
            var s1 = new TensorShape3D(2, 3, 4);
            var s2 = new TensorShape3D(1, 3, 4);
            var s3 = new TensorShape3D(2, 1, 4);
            var s4 = new TensorShape3D(2, 3, 1);

            Assert.True(TensorShape3D.CanBroadcast(s1, s2));
            Assert.True(TensorShape3D.CanBroadcast(s1, s3));
            Assert.True(TensorShape3D.CanBroadcast(s1, s4));
            Assert.True(TensorShape3D.CanBroadcast(s1, s1));
        }

        [Fact]
        public void TestBroadcast()
        {
            var s1 = new TensorShape3D(2, 3, 4);
            var s2 = new TensorShape3D(1, 3, 4);

            var result = TensorShape3D.Broadcast(s1, s2);
            Assert.Equal(new TensorShape3D(2, 3, 4), result);
        }
    }
}
