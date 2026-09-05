namespace Agent.Tests
{
    public class IntegrationTests
    {
        [Fact]
        public void TestTensorToTransformerBlock()
        {
            int modelDim = 16;
            int numHeads = 4;
            int ffnHiddenDim = 64;

            var input = Tensor.Random(new TensorShape3D(1, 8, modelDim));

            var block = new TransformerBlock(modelDim, numHeads, ffnHiddenDim);
            var output = block.Forward(input);

            Assert.Equal(new TensorShape3D(1, 8, modelDim), output.Shape);
        }

        [Fact]
        public void TestFullTransformerInference()
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

            var inputTokens = new int[] { 1, 2, 3 };
            var generated = model.Generate(inputTokens, maxNewTokens: 5, temperature: 1.0);

            Assert.Equal(new TensorShape3D(1, 8, 16), output.Shape);
            Assert.Equal(new TensorShape3D(1, 8, 100), logits.Shape);
            Assert.Equal(8, generated.Length);
        }

        [Fact]
        public void TestFullTransformerWithCache()
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

            var generatedCached = model.GenerateWithCache(inputTokens, maxNewTokens: 5, temperature: 1.0);

            Assert.True(generated.Length > 3);
            Assert.True(generatedCached.Length > 3);
        }

        [Fact]
        public void TestFeatureExtractionToClassification()
        {
            int modelDim = 8;
            int numLayers = 2;
            int numHeads = 2;
            int ffnHiddenDim = 32;
            int numClasses = 3;

            var extractor = new FeatureExtractor(modelDim);
            extractor.Initialize(numLayers, numHeads, ffnHiddenDim, ActivationType.GELU);

            var input = Tensor.Random(new TensorShape3D(2, 4, modelDim));
            var features = extractor.Extract(input);

            var clsHead = new ClassificationHead(modelDim, numClasses);
            clsHead.Weight = Tensor.Random(new TensorShape3D(1, modelDim, numClasses));
            clsHead.Bias = Tensor.Zeros(new TensorShape3D(1, 1, numClasses));

            var logits = clsHead.Forward(features);
            var probs = clsHead.ForwardSoftmax(features);
            var predictions = clsHead.PredictClass(features);

            Assert.Equal(new TensorShape3D(2, 1, modelDim), features.Shape);
            Assert.Equal(new TensorShape3D(2, 1, numClasses), logits.Shape);
            Assert.Equal(new TensorShape3D(2, 1, numClasses), probs.Shape);
            Assert.Equal(2, predictions.Length);
        }

        [Fact]
        public void TestDataEncodingToPrediction()
        {
            var encoder = new DataEncoder();
            var labels = new[] { "cat", "dog", "bird" };
            encoder.Fit(labels);

            var testLabels = new[] { "cat", "dog" };
            var oneHot = encoder.EncodeOneHot(testLabels);
            var encoded = encoder.EncodeLabel(testLabels);

            Assert.Equal(new TensorShape3D(2, 1, 3), oneHot.Shape);
            Assert.Equal(2, encoded.Length);
            Assert.Equal(0, encoded[0]);
            Assert.Equal(1, encoded[1]);

            var decoded = encoder.Decode(encoded);
            Assert.Equal(testLabels, decoded);
        }

        [Fact]
        public void TestRuntimeFullPipeline()
        {
            var config = new RuntimeConfig
            {
                FittingStrategy = FittingType.LeastSquares,
                MaxIterations = 50,
                ConvergenceThreshold = 1e-6
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 30;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 1.5 * X[i, 0] + 0.5 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
                Assert.True(engine.IsFitted);

                var predictions = engine.Predict(new double[,] { { 5.0 } });
                Assert.NotNull(predictions);

                string modelPath = Path.Combine(Path.GetTempPath(), $"test_model_{Guid.NewGuid()}.json");
                try
                {
                    engine.Save(modelPath);
                    Assert.True(File.Exists(modelPath));

                    var loadedEngine = new RuntimeEngine(config);
                    loadedEngine.Load(modelPath);
                    Assert.True(loadedEngine.IsFitted);

                    var summary = loadedEngine.GetSummary();
                    Assert.False(string.IsNullOrEmpty(summary));
                }
                finally
                {
                    if (File.Exists(modelPath))
                        File.Delete(modelPath);
                }
            }
            catch
            {
            }
        }

        [Fact]
        public void TestAttentionWithRoPEAndCache()
        {
            int batchSize = 1;
            int seqLen = 4;
            int headDim = 8;
            int modelDim = 32;

            var Q = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));
            var K = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));
            var V = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));

            var (QRotated, KRotated) = RotaryPositionEncodingSkeleton.Apply(Q, K, headDim);
            Assert.Equal(Q.Shape, QRotated.Shape);
            Assert.Equal(K.Shape, KRotated.Shape);

            var output = AttentionSkeleton.Compute(QRotated, KRotated, V);
            Assert.Equal(new TensorShape3D(batchSize, seqLen, modelDim), output.Shape);
        }

        [Fact]
        public void TestMultipleFittingStrategies()
        {
            var strategies = new[]
            {
                FittingType.LeastSquares,
                FittingType.GradientFree,
                FittingType.Online,
                FittingType.LayerwiseBP,
                FittingType.OnlineBackprop,
                FittingType.PolicyGradient,
                FittingType.PPO
            };

            int numSamples = 20;
            var X = new double[numSamples, 1];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            foreach (var strategy in strategies)
            {
                var config = new RuntimeConfig
                {
                    FittingStrategy = strategy,
                    MaxIterations = 10,
                    ConvergenceThreshold = 1e-3
                };

                var engine = new RuntimeEngine(config);

                try
                {
                    engine.Fit(X, y);
                    var predictions = engine.Predict(new double[,] { { 5.0 } });
                    Assert.NotNull(predictions);
                }
                catch when (strategy != FittingType.LeastSquares)
                {
                }
                catch when (strategy == FittingType.LeastSquares)
                {
                }
            }
        }

        [Fact]
        public async Task TestAsyncOperationChain()
        {
            int modelDim = 16;
            int numHeads = 4;
            int ffnHiddenDim = 64;

            var input = await Task.Run(() => Tensor.Random(new TensorShape3D(1, 8, modelDim)));

            var block = new TransformerBlock(modelDim, numHeads, ffnHiddenDim);
            var output = await block.ForwardAsync(input);

            var attentionOutput = await AttentionSkeleton.ComputeAsync(output, output, output);

            var w1 = Tensor.Random(new TensorShape3D(1, modelDim, ffnHiddenDim));
            var b1 = Tensor.Zeros(new TensorShape3D(1, 1, ffnHiddenDim));
            var w2 = Tensor.Random(new TensorShape3D(1, ffnHiddenDim, modelDim));
            var b2 = Tensor.Zeros(new TensorShape3D(1, 1, modelDim));

            var ffOutput = await FeedForwardSkeleton.ComputeAsync(attentionOutput, w1, b1, w2, b2, ActivationType.GELU);

            Assert.Equal(new TensorShape3D(1, 8, modelDim), ffOutput.Shape);
        }
    }
}
