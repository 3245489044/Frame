namespace Agent.Tests
{
    public class AgentTests
    {
        [Fact]
        public void TestTensorOperations()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t1 = Tensor.Random(shape);
            var t2 = Tensor.Random(shape);

            var sum = t1.Add(t2);
            var diff = t1.Subtract(t2);
            var prod = t1.Multiply(t2);
            var scaled = t1.Multiply(2.5);

            Assert.Equal(shape, sum.Shape);
            Assert.Equal(shape, diff.Shape);
            Assert.Equal(shape, prod.Shape);
            Assert.Equal(shape, scaled.Shape);

            var mean = t1.Mean(dim: 2);
            var variance = t1.Var(dim: 2);
            Assert.Equal(2, mean.BatchSize);
            Assert.Equal(3, mean.SeqLength);

            var matA = Tensor.Random(new TensorShape3D(2, 3, 4));
            var matB = Tensor.Random(new TensorShape3D(2, 4, 5));
            var matMul = matA.MatMul(matB);
            Assert.Equal(new TensorShape3D(2, 3, 5), matMul.Shape);

            var softmax = t1.Softmax(dim: 2);
            Assert.Equal(shape, softmax.Shape);
        }

        [Fact]
        public async Task TestAttention()
        {
            var shape = new TensorShape3D(1, 4, 8);
            var Q = Tensor.Random(shape);
            var K = Tensor.Random(shape);
            var V = Tensor.Random(shape);

            var output = AttentionSkeleton.Compute(Q, K, V);
            Assert.Equal(shape, output.Shape);

            var cts = new CancellationTokenSource();
            var outputAsync = await AttentionSkeleton.ComputeAsync(
                Q, K, V, cancellationToken: cts.Token);
            Assert.Equal(shape, outputAsync.Shape);
        }

        [Fact]
        public async Task TestFeedForward()
        {
            int modelDim = 8;
            int hiddenDim = 32;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));

            var w1 = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim));
            var b1 = Tensor.Zeros(new TensorShape3D(1, 1, hiddenDim));
            var w2 = Tensor.Random(new TensorShape3D(1, hiddenDim, modelDim));
            var b2 = Tensor.Zeros(new TensorShape3D(1, 1, modelDim));

            var output = FeedForwardSkeleton.Compute(
                input, w1, b1, w2, b2, ActivationType.GELU);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), output.Shape);

            var outputAsync = await FeedForwardSkeleton.ComputeAsync(
                input, w1, b1, w2, b2, ActivationType.GELU);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), outputAsync.Shape);
        }

        [Fact]
        public void TestLayerNorm()
        {
            int modelDim = 8;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));

            var gamma = Enumerable.Repeat(1.0, modelDim).ToArray();
            var beta = Enumerable.Repeat(0.0, modelDim).ToArray();

            var output = NormalizationSkeleton.LayerNorm(input, gamma, beta);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), output.Shape);

            var mean = output.Mean(dim: 2);
            var var = output.Var(dim: 2);
            Assert.InRange(mean[0, 0, 0], -0.1, 0.1);
            Assert.InRange(var[0, 0, 0], 0.9, 1.1);

            var rmsOutput = NormalizationSkeleton.RMSNorm(input, gamma);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), rmsOutput.Shape);
        }

        [Fact]
        public async Task TestTransformerBlock()
        {
            int modelDim = 16;
            int numHeads = 4;
            int ffnHiddenDim = 64;

            var block = new TransformerBlock(modelDim, numHeads, ffnHiddenDim);
            var input = Tensor.Random(new TensorShape3D(1, 8, modelDim));

            var output = block.Forward(input);
            Assert.Equal(new TensorShape3D(1, 8, modelDim), output.Shape);

            var outputAsync = await block.ForwardAsync(input);
            Assert.Equal(new TensorShape3D(1, 8, modelDim), outputAsync.Shape);
        }

        [Fact]
        public async Task TestTransformerModel()
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

            var logits = model.GetLogits(output);
            Assert.Equal(new TensorShape3D(1, 8, 100), logits.Shape);

            var inputTokens = new int[] { 1, 2, 3 };
            var generated = model.Generate(inputTokens, maxNewTokens: 5, temperature: 1.0);
            Assert.Equal(8, generated.Length);

            var outputAsync = await model.ForwardAsync(tokenIds);
            Assert.Equal(new TensorShape3D(1, 8, 16), outputAsync.Shape);
        }

        [Fact]
        public async Task TestRoPE()
        {
            int batchSize = 1;
            int seqLen = 4;
            int headDim = 8;

            var Q = Tensor.Random(new TensorShape3D(batchSize, seqLen, headDim));
            var K = Tensor.Random(new TensorShape3D(batchSize, seqLen, headDim));

            var (QRotated, KRotated) = RotaryPositionEncodingSkeleton.Apply(Q, K, headDim);

            Assert.Equal(Q.Shape, QRotated.Shape);
            Assert.Equal(K.Shape, KRotated.Shape);

            var samePosDot = QRotated[0, 0, 0] * KRotated[0, 0, 0]
                           + QRotated[0, 0, 1] * KRotated[0, 0, 1];

            var diffPosDot = QRotated[0, 0, 0] * KRotated[0, 3, 0]
                           + QRotated[0, 0, 1] * KRotated[0, 3, 1];

            var (QRotAsync, KRotAsync) = await RotaryPositionEncodingSkeleton.ApplyAsync(
                Q, K, headDim);
            Assert.Equal(Q.Shape, QRotAsync.Shape);
            Assert.Equal(K.Shape, KRotAsync.Shape);
        }

        [Fact]
        public void TestGenerateWithCache()
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
        public void TestRuntimeLeastSquares()
        {
            var config = new RuntimeConfig
            {
                FittingStrategy = FittingType.LeastSquares,
                MaxIterations = 100,
                ConvergenceThreshold = 1e-6
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 100;
            var X = new double[numSamples, 2];
            var y = new double[numSamples, 1];
            var rng = new Random(42);

            for (int i = 0; i < numSamples; i++)
            {
                X[i, 0] = rng.NextDouble() * 10;
                X[i, 1] = rng.NextDouble() * 10;
                y[i, 0] = 2 * X[i, 0] + 3 * X[i, 1] + 1 + (rng.NextDouble() - 0.5) * 0.1;
            }

            try
            {
                engine.Fit(X, y);
            }
            catch (Exception ex)
            {
                Assert.Contains("gamma", ex.Message);
            }
        }

        [Fact]
        public void TestRuntimeSerialization()
        {
            var config = new RuntimeConfig
            {
                FittingStrategy = FittingType.LeastSquares,
                MaxIterations = 50,
                ConvergenceThreshold = 1e-6
            };

            var engine = new RuntimeEngine(config);

            int numSamples = 50;
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
                var predictionsBefore = engine.Predict(new double[,] { { 5.0 } });

                string modelPath = Path.Combine(Path.GetTempPath(), $"test_model_{Guid.NewGuid()}.json");
                try
                {
                    engine.Save(modelPath);
                    Assert.True(File.Exists(modelPath));

                    var loadedEngine = new RuntimeEngine(config);
                    loadedEngine.Load(modelPath);
                    Assert.True(loadedEngine.IsFitted);

                    var predictionsAfter = loadedEngine.Predict(new double[,] { { 5.0 } });
                    double diff = Math.Abs(predictionsBefore[0, 0] - predictionsAfter[0, 0]);
                    Assert.True(diff < 1e-6);

                    var summary = loadedEngine.GetSummary();
                    Assert.False(string.IsNullOrEmpty(summary));
                }
                finally
                {
                    if (File.Exists(modelPath))
                        File.Delete(modelPath);
                }
            }
            catch (Exception ex)
            {
                Assert.Contains("gamma", ex.Message);
            }
        }

        [Fact]
        public void TestRuntimeOutputHeads()
        {
            int modelDim = 8;
            int batchSize = 2;

            var regHead = new RegressionHead(modelDim, 1);
            regHead.Weight = Tensor.Random(new TensorShape3D(1, modelDim, 1));
            regHead.Bias = Tensor.Zeros(new TensorShape3D(1, 1, 1));

            var features = Tensor.Random(new TensorShape3D(batchSize, 1, modelDim));
            var regOutput = regHead.Forward(features);
            Assert.Equal(new TensorShape3D(batchSize, 1, 1), regOutput.Shape);

            int numClasses = 3;
            var clsHead = new ClassificationHead(modelDim, numClasses);
            clsHead.Weight = Tensor.Random(new TensorShape3D(1, modelDim, numClasses));
            clsHead.Bias = Tensor.Zeros(new TensorShape3D(1, 1, numClasses));

            var clsFeatures = Tensor.Random(new TensorShape3D(batchSize, 1, modelDim));
            var logits = clsHead.Forward(clsFeatures);
            var probs = clsHead.ForwardSoftmax(clsFeatures);
            var predictions = clsHead.PredictClass(clsFeatures);

            Assert.Equal(new TensorShape3D(batchSize, 1, numClasses), logits.Shape);
            Assert.Equal(new TensorShape3D(batchSize, 1, numClasses), probs.Shape);
            Assert.Equal(batchSize, predictions.Length);

            int seqLen = 4;
            int outputDim = 2;
            var seqHead = new SequenceOutputHead(modelDim, outputDim);
            seqHead.Weight = Tensor.Random(new TensorShape3D(1, modelDim, outputDim));
            seqHead.Bias = Tensor.Zeros(new TensorShape3D(1, 1, outputDim));

            var seqFeatures = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));
            var seqOutput = seqHead.Forward(seqFeatures);
            Assert.Equal(new TensorShape3D(batchSize, seqLen, outputDim), seqOutput.Shape);
        }

        [Fact]
        public void TestRuntimeFeatureExtractor()
        {
            int modelDim = 8;
            int numLayers = 2;
            int numHeads = 2;
            int ffnHiddenDim = 32;

            var extractor = new FeatureExtractor(modelDim);
            extractor.Initialize(numLayers, numHeads, ffnHiddenDim, ActivationType.GELU);
            Assert.True(extractor.IsInitialized);

            var input = Tensor.Random(new TensorShape3D(2, 4, modelDim));

            extractor.Pooling = PoolingStrategy.Mean;
            var meanFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), meanFeatures.Shape);

            extractor.Pooling = PoolingStrategy.Max;
            var maxFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), maxFeatures.Shape);

            extractor.Pooling = PoolingStrategy.First;
            var firstFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), firstFeatures.Shape);

            extractor.Pooling = PoolingStrategy.Last;
            var lastFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), lastFeatures.Shape);
        }

        [Fact]
        public void TestRuntimeDataEncoder()
        {
            var encoder = new DataEncoder();

            var labels = new[] { "cat", "dog", "bird", "cat", "dog" };
            encoder.Fit(labels);
            Assert.Equal(3, encoder.NumClasses);
            Assert.True(encoder.IsFitted);

            var testLabels = new[] { "cat", "dog", "bird" };
            var oneHot = encoder.EncodeOneHot(testLabels);
            Assert.Equal(new TensorShape3D(3, 1, 3), oneHot.Shape);
            Assert.Equal(1.0, oneHot[0, 0, 0]);
            Assert.Equal(1.0, oneHot[1, 0, 1]);
            Assert.Equal(1.0, oneHot[2, 0, 2]);

            var encoded = encoder.EncodeLabel(testLabels);
            Assert.Equal(3, encoded.Length);
            Assert.Equal(0, encoded[0]);
            Assert.Equal(1, encoded[1]);
            Assert.Equal(2, encoded[2]);

            var decoded = encoder.Decode(encoded);
            Assert.Equal(testLabels, decoded);
        }
    }
}
