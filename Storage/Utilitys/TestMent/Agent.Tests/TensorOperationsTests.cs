namespace Agent.Tests
{
    public class TensorOperationsTests
    {
        [Fact]
        public void TestSplitHeads()
        {
            int batchSize = 2;
            int seqLen = 4;
            int modelDim = 8;
            int numHeads = 2;

            var input = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));
            var heads = TensorOperations.SplitHeads(input, numHeads);

            Assert.Equal(numHeads, heads.Length);
            foreach (var head in heads)
            {
                Assert.Equal(new TensorShape3D(batchSize, seqLen, modelDim / numHeads), head.Shape);
            }
        }

        [Fact]
        public void TestConcatHeads()
        {
            int batchSize = 2;
            int seqLen = 4;
            int headDim = 4;
            int numHeads = 2;

            var heads = new Tensor[numHeads];
            for (int h = 0; h < numHeads; h++)
            {
                heads[h] = Tensor.Random(new TensorShape3D(batchSize, seqLen, headDim));
            }

            var concat = TensorOperations.ConcatHeads(heads);
            Assert.Equal(new TensorShape3D(batchSize, seqLen, headDim * numHeads), concat.Shape);
        }

        [Fact]
        public void TestSplitAndConcatRoundTrip()
        {
            int batchSize = 2;
            int seqLen = 4;
            int modelDim = 8;
            int numHeads = 2;

            var input = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));
            var heads = TensorOperations.SplitHeads(input, numHeads);
            var output = TensorOperations.ConcatHeads(heads);

            Assert.Equal(input.Shape, output.Shape);
            for (int b = 0; b < batchSize; b++)
                for (int s = 0; s < seqLen; s++)
                    for (int d = 0; d < modelDim; d++)
                        Assert.Equal(input[b, s, d], output[b, s, d], 5);
        }

        [Fact]
        public void TestSplitHeadsInvalidNumHeads()
        {
            var input = Tensor.Random(new TensorShape3D(2, 4, 8));
            Assert.ThrowsAny<Exception>(() => TensorOperations.SplitHeads(input, 3));
        }

        [Fact]
        public void TestNullInputThrows()
        {
            Assert.Throws<ArgumentNullException>(() => TensorOperations.SplitHeads(null!, 2));
            Assert.Throws<ArgumentNullException>(() => TensorOperations.ConcatHeads(null!));
        }
    }
}
