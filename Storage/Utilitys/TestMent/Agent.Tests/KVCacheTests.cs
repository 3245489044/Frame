namespace Agent.Tests
{
    public class KVCacheTests
    {
        [Fact]
        public void TestCreateAndAppend()
        {
            int batchSize = 1;
            int dimension = 32;
            int numLayers = 2;
            int maxCacheLen = 16;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);
            Assert.Equal(0, cache.CurrentLength);

            var K = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
            var V = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
            cache.Append(0, K, V);

            Assert.Equal(1, cache.CurrentLength);

            var cachedK = cache.GetK(0);
            var cachedV = cache.GetV(0);
            Assert.NotNull(cachedK);
            Assert.NotNull(cachedV);
        }

        [Fact]
        public void TestMultipleAppends()
        {
            int batchSize = 1;
            int dimension = 8;
            int numLayers = 1;
            int maxCacheLen = 8;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);

            for (int i = 0; i < 5; i++)
            {
                var K = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
                var V = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
                cache.Append(0, K, V);
            }

            Assert.Equal(5, cache.CurrentLength);

            var cachedK = cache.GetK(0);
            Assert.Equal(new TensorShape3D(batchSize, 5, dimension), cachedK!.Shape);
        }

        [Fact]
        public void TestReset()
        {
            int batchSize = 1;
            int dimension = 8;
            int numLayers = 1;
            int maxCacheLen = 8;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);

            var K = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
            var V = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
            cache.Append(0, K, V);
            Assert.Equal(1, cache.CurrentLength);

            cache.Reset();
            Assert.Equal(0, cache.CurrentLength);
        }

        [Fact]
        public void TestMaxLengthTruncation()
        {
            int batchSize = 1;
            int dimension = 8;
            int numLayers = 1;
            int maxCacheLen = 3;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);

            for (int i = 0; i < 5; i++)
            {
                var K = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
                var V = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
                cache.Append(0, K, V);
            }

            Assert.Equal(3, cache.CurrentLength);
        }

        [Fact]
        public void TestMultipleLayers()
        {
            int batchSize = 1;
            int dimension = 8;
            int numLayers = 3;
            int maxCacheLen = 8;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);

            for (int layer = 0; layer < numLayers; layer++)
            {
                var K = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
                var V = Tensor.Random(new TensorShape3D(batchSize, 1, dimension));
                cache.Append(layer, K, V);
            }

            Assert.Equal(1, cache.CurrentLength);

            for (int layer = 0; layer < numLayers; layer++)
            {
                Assert.NotNull(cache.GetK(layer));
                Assert.NotNull(cache.GetV(layer));
            }
        }

        [Fact]
        public void TestNullInputThrows()
        {
            int batchSize = 1;
            int dimension = 8;
            int numLayers = 1;
            int maxCacheLen = 8;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);

            Assert.Throws<ArgumentNullException>(() => cache.Append(0, null!, Tensor.Random(new TensorShape3D(1, 1, dimension))));
            Assert.Throws<ArgumentNullException>(() => cache.Append(0, Tensor.Random(new TensorShape3D(1, 1, dimension)), null!));
        }

        [Fact]
        public void TestInvalidLayerIndex()
        {
            int batchSize = 1;
            int dimension = 8;
            int numLayers = 2;
            int maxCacheLen = 8;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);
            var valid = Tensor.Random(new TensorShape3D(1, 1, dimension));

            Assert.Throws<IndexOutOfRangeException>(() => cache.Append(5, valid, valid));
            Assert.Throws<IndexOutOfRangeException>(() => cache.GetK(5));
            Assert.Throws<IndexOutOfRangeException>(() => cache.GetV(-1));
        }

        [Fact]
        public void TestMaxCacheLenProperty()
        {
            int batchSize = 1;
            int dimension = 8;
            int numLayers = 1;
            int maxCacheLen = 16;

            var cache = new KVCache(batchSize, dimension, numLayers, maxCacheLen);
            Assert.Equal(maxCacheLen, cache.MaxCacheLen);
        }

        [Fact]
        public void TestInvalidConstructorParams()
        {
            Assert.Throws<ArgumentException>(() => new KVCache(0, 8, 1));
            Assert.Throws<ArgumentException>(() => new KVCache(1, 0, 1));
            Assert.Throws<ArgumentException>(() => new KVCache(1, 8, 0));
            Assert.Throws<ArgumentException>(() => new KVCache(1, 8, 1, 0));
        }
    }
}
