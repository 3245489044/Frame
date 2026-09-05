namespace Agent.Tests
{
    public class ModelTests
    {
        [Fact]
        public void TestTransformerModelForward()
        {
            var config = new TransformerConfig
            {
                VocabSize = 100,
                MaxSeqLen = 16,
                ModelDim = 16,
                NumHeads = 4,
                NumLayers = 2,
                Seed = 42
            };

            var model = new TransformerModel(config);

            var tokenIds = new int[,] { { 1, 2, 3, 4, 5, 0, 0, 0 } };
            var output = model.Forward(tokenIds);
            Assert.Equal(new TensorShape3D(1, 8, 16), output.Shape);
        }

        [Fact]
        public void TestTransformerModelGetLogits()
        {
            var config = new TransformerConfig
            {
                VocabSize = 100,
                MaxSeqLen = 16,
                ModelDim = 16,
                NumHeads = 4,
                NumLayers = 2,
                Seed = 42
            };

            var model = new TransformerModel(config);

            var tokenIds = new int[,] { { 1, 2, 3, 4, 5, 0, 0, 0 } };
            var output = model.Forward(tokenIds);
            var logits = model.GetLogits(output);
            Assert.Equal(new TensorShape3D(1, 8, 100), logits.Shape);
        }

        [Fact]
        public void TestTransformerModelGenerate()
        {
            var config = new TransformerConfig
            {
                VocabSize = 100,
                MaxSeqLen = 16,
                ModelDim = 16,
                NumHeads = 4,
                NumLayers = 2,
                Seed = 42
            };

            var model = new TransformerModel(config);

            var inputTokens = new int[] { 1, 2, 3 };
            var generated = model.Generate(inputTokens, maxNewTokens: 5, temperature: 1.0);
            Assert.Equal(8, generated.Length);
        }

        [Fact]
        public async Task TestTransformerModelForwardAsync()
        {
            var config = new TransformerConfig
            {
                VocabSize = 100,
                MaxSeqLen = 16,
                ModelDim = 16,
                NumHeads = 4,
                NumLayers = 2,
                Seed = 42
            };

            var model = new TransformerModel(config);

            var tokenIds = new int[,] { { 1, 2, 3, 4, 5, 0, 0, 0 } };
            var output = await model.ForwardAsync(tokenIds);
            Assert.Equal(new TensorShape3D(1, 8, 16), output.Shape);
        }

        [Fact]
        public void TestTransformerModelGenerateWithCache()
        {
            var config = new TransformerConfig
            {
                VocabSize = 100,
                MaxSeqLen = 16,
                ModelDim = 16,
                NumHeads = 4,
                NumLayers = 2,
                Seed = 42
            };

            var model = new TransformerModel(config);

            var inputTokens = new int[] { 1, 2, 3 };

            var generated = model.Generate(inputTokens, maxNewTokens: 5, temperature: 1.0);
            Assert.True(generated.Length > 3);

            var generatedCached = model.GenerateWithCache(inputTokens, maxNewTokens: 5, temperature: 1.0);
            Assert.True(generatedCached.Length > 3);
        }

        [Fact]
        public async Task TestTransformerModelNullInputThrows()
        {
            var config = new TransformerConfig
            {
                VocabSize = 100,
                MaxSeqLen = 16,
                ModelDim = 16,
                NumHeads = 4,
                NumLayers = 2,
                Seed = 42
            };

            var model = new TransformerModel(config);

            Assert.Throws<ArgumentNullException>(() => model.Forward(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => model.ForwardAsync(null!));
        }

        [Fact]
        public void TestTransformerModelInvalidConfig()
        {
            Assert.Throws<ArgumentException>(() => new TransformerModel(new TransformerConfig { VocabSize = 0 }));
            Assert.Throws<ArgumentException>(() => new TransformerModel(new TransformerConfig { VocabSize = 100, ModelDim = 0 }));
        }

        [Fact]
        public void TestTransformerModelDifferentConfigurations()
        {
            var configs = new[]
            {
                new TransformerConfig { VocabSize = 50, MaxSeqLen = 8, ModelDim = 8, NumHeads = 2, NumLayers = 1, Seed = 42 },
                new TransformerConfig { VocabSize = 200, MaxSeqLen = 32, ModelDim = 32, NumHeads = 8, NumLayers = 3, Seed = 123 },
            };

            foreach (var config in configs)
            {
                var model = new TransformerModel(config);
                var tokenIds = new int[,] { { 1, 2, 3 } };
                var output = model.Forward(tokenIds);
                Assert.Equal(new TensorShape3D(1, 3, config.ModelDim), output.Shape);
            }
        }
    }
}
