namespace Agent.Tests
{
    public class LayerTests
    {
        [Fact]
        public void TestTransformerBlockForward()
        {
            int modelDim = 16;
            int numHeads = 4;
            int ffnHiddenDim = 64;

            var block = new TransformerBlock(modelDim, numHeads, ffnHiddenDim);
            var input = Tensor.Random(new TensorShape3D(1, 8, modelDim));

            var output = block.Forward(input);
            Assert.Equal(new TensorShape3D(1, 8, modelDim), output.Shape);
        }

        [Fact]
        public async Task TestTransformerBlockForwardAsync()
        {
            int modelDim = 16;
            int numHeads = 4;
            int ffnHiddenDim = 64;

            var block = new TransformerBlock(modelDim, numHeads, ffnHiddenDim);
            var input = Tensor.Random(new TensorShape3D(1, 8, modelDim));

            var output = await block.ForwardAsync(input);
            Assert.Equal(new TensorShape3D(1, 8, modelDim), output.Shape);
        }

        [Fact]
        public void TestTransformerBlockNullInputThrows()
        {
            int modelDim = 16;
            int numHeads = 4;
            int ffnHiddenDim = 64;

            var block = new TransformerBlock(modelDim, numHeads, ffnHiddenDim);
            Assert.Throws<ArgumentNullException>(() => block.Forward(null!));
        }

        [Fact]
        public void TestMultiHeadAttentionLayer()
        {
            int modelDim = 16;
            int numHeads = 4;

            var mha = new MultiHeadAttentionLayer(modelDim, numHeads);
            var input = Tensor.Random(new TensorShape3D(1, 8, modelDim));

            var output = mha.Forward(input);
            Assert.Equal(new TensorShape3D(1, 8, modelDim), output.Shape);
        }

        [Fact]
        public void TestMultiHeadAttentionLayerNullInputThrows()
        {
            var mha = new MultiHeadAttentionLayer(16, 4);
            Assert.Throws<ArgumentNullException>(() => mha.Forward(null!));
        }

        [Fact]
        public void TestFeedForwardLayer()
        {
            int modelDim = 16;
            int hiddenDim = 64;

            var layer = new FeedForwardLayer(modelDim, hiddenDim);
            var input = Tensor.Random(new TensorShape3D(1, 8, modelDim));

            var output = layer.Forward(input);
            Assert.Equal(new TensorShape3D(1, 8, modelDim), output.Shape);
        }

        [Fact]
        public void TestFeedForwardLayerNullInputThrows()
        {
            var layer = new FeedForwardLayer(16, 64);
            Assert.Throws<ArgumentNullException>(() => layer.Forward(null!));
        }
    }
}
