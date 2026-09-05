namespace Agent.Tests
{
    public class SkeletonTests
    {
        [Fact]
        public void TestAttentionSkeletonCompute()
        {
            var shape = new TensorShape3D(1, 4, 8);
            var Q = Tensor.Random(shape);
            var K = Tensor.Random(shape);
            var V = Tensor.Random(shape);

            var output = AttentionSkeleton.Compute(Q, K, V);
            Assert.Equal(shape, output.Shape);
        }

        [Fact]
        public void TestAttentionSkeletonComputeWithWeights()
        {
            var shape = new TensorShape3D(1, 4, 8);
            var Q = Tensor.Random(shape);
            var K = Tensor.Random(shape);
            var V = Tensor.Random(shape);

            var output = AttentionSkeleton.ComputeWithWeights(Q, K, V, out var weights);
            Assert.Equal(shape, output.Shape);
            Assert.NotNull(weights);
            Assert.Equal(new TensorShape3D(1, 4, 4), weights.Shape);
        }

        [Fact]
        public async Task TestAttentionSkeletonComputeAsync()
        {
            var shape = new TensorShape3D(1, 4, 8);
            var Q = Tensor.Random(shape);
            var K = Tensor.Random(shape);
            var V = Tensor.Random(shape);

            var cts = new CancellationTokenSource();
            var output = await AttentionSkeleton.ComputeAsync(Q, K, V, cancellationToken: cts.Token);
            Assert.Equal(shape, output.Shape);
        }

        [Fact]
        public void TestAttentionSkeletonWithMask()
        {
            var shape = new TensorShape3D(1, 4, 8);
            var Q = Tensor.Random(shape);
            var K = Tensor.Random(shape);
            var V = Tensor.Random(shape);
            var mask = Tensor.Zeros(new TensorShape3D(1, 4, 4));

            var output = AttentionSkeleton.Compute(Q, K, V, mask);
            Assert.Equal(shape, output.Shape);
        }

        [Fact]
        public void TestAttentionSkeletonNullInputThrows()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 4, 8));
            Assert.Throws<ArgumentNullException>(() => AttentionSkeleton.Compute(null!, valid, valid));
            Assert.Throws<ArgumentNullException>(() => AttentionSkeleton.Compute(valid, null!, valid));
            Assert.Throws<ArgumentNullException>(() => AttentionSkeleton.Compute(valid, valid, null!));
        }

        [Fact]
        public void TestFeedForwardSkeletonCompute()
        {
            int modelDim = 8;
            int hiddenDim = 32;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));

            var w1 = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim));
            var b1 = Tensor.Zeros(new TensorShape3D(1, 1, hiddenDim));
            var w2 = Tensor.Random(new TensorShape3D(1, hiddenDim, modelDim));
            var b2 = Tensor.Zeros(new TensorShape3D(1, 1, modelDim));

            var output = FeedForwardSkeleton.Compute(input, w1, b1, w2, b2, ActivationType.GELU);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), output.Shape);
        }

        [Fact]
        public async Task TestFeedForwardSkeletonComputeAsync()
        {
            int modelDim = 8;
            int hiddenDim = 32;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));

            var w1 = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim));
            var b1 = Tensor.Zeros(new TensorShape3D(1, 1, hiddenDim));
            var w2 = Tensor.Random(new TensorShape3D(1, hiddenDim, modelDim));
            var b2 = Tensor.Zeros(new TensorShape3D(1, 1, modelDim));

            var output = await FeedForwardSkeleton.ComputeAsync(input, w1, b1, w2, b2, ActivationType.GELU);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), output.Shape);
        }

        [Fact]
        public void TestFeedForwardSkeletonNullInputThrows()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 4, 8));
            var w = Tensor.Random(new TensorShape3D(1, 8, 32));
            var b = Tensor.Zeros(new TensorShape3D(1, 1, 32));

            Assert.Throws<ArgumentNullException>(() => FeedForwardSkeleton.Compute(null!, w, b, w, b, ActivationType.GELU));
            Assert.Throws<ArgumentNullException>(() => FeedForwardSkeleton.Compute(valid, null!, b, w, b, ActivationType.GELU));
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
        }

        [Fact]
        public void TestRMSNorm()
        {
            int modelDim = 8;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));
            var gamma = Enumerable.Repeat(1.0, modelDim).ToArray();

            var output = NormalizationSkeleton.RMSNorm(input, gamma);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), output.Shape);
        }

        [Fact]
        public void TestNormalizationNullInputThrows()
        {
            var gamma = new double[] { 1.0, 1.0 };
            var beta = new double[] { 0.0, 0.0 };

            Assert.Throws<ArgumentNullException>(() => NormalizationSkeleton.LayerNorm(null!, gamma, beta));
            Assert.Throws<ArgumentNullException>(() => NormalizationSkeleton.RMSNorm(null!, gamma));
        }

        [Fact]
        public void TestRoPE()
        {
            int batchSize = 1;
            int seqLen = 4;
            int headDim = 8;

            var Q = Tensor.Random(new TensorShape3D(batchSize, seqLen, headDim));
            var K = Tensor.Random(new TensorShape3D(batchSize, seqLen, headDim));

            var (QRotated, KRotated) = RotaryPositionEncodingSkeleton.Apply(Q, K, headDim);

            Assert.Equal(Q.Shape, QRotated.Shape);
            Assert.Equal(K.Shape, KRotated.Shape);
        }

        [Fact]
        public async Task TestRoPEAsync()
        {
            int batchSize = 1;
            int seqLen = 4;
            int headDim = 8;

            var Q = Tensor.Random(new TensorShape3D(batchSize, seqLen, headDim));
            var K = Tensor.Random(new TensorShape3D(batchSize, seqLen, headDim));

            var (QRotAsync, KRotAsync) = await RotaryPositionEncodingSkeleton.ApplyAsync(Q, K, headDim);
            Assert.Equal(Q.Shape, QRotAsync.Shape);
            Assert.Equal(K.Shape, KRotAsync.Shape);
        }

        [Fact]
        public void TestRoPENullInputThrows()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 4, 8));
            Assert.Throws<ArgumentNullException>(() => RotaryPositionEncodingSkeleton.Apply(null!, valid, 8));
            Assert.Throws<ArgumentNullException>(() => RotaryPositionEncodingSkeleton.Apply(valid, null!, 8));
        }

        [Fact]
        public void TestResidualPreLN()
        {
            int modelDim = 8;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));
            var gamma = Enumerable.Repeat(1.0, modelDim).ToArray();
            var beta = Enumerable.Repeat(0.0, modelDim).ToArray();

            Tensor Sublayer(Tensor x) => x;

            var output = ResidualSkeleton.Apply(input, Sublayer, gamma, beta, preNorm: true);
            Assert.Equal(input.Shape, output.Shape);
        }

        [Fact]
        public void TestResidualPostLN()
        {
            int modelDim = 8;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));
            var gamma = Enumerable.Repeat(1.0, modelDim).ToArray();
            var beta = Enumerable.Repeat(0.0, modelDim).ToArray();

            Tensor Sublayer(Tensor x) => x;

            var output = ResidualSkeleton.Apply(input, Sublayer, gamma, beta, preNorm: false);
            Assert.Equal(input.Shape, output.Shape);
        }

        [Fact]
        public async Task TestResidualAsync()
        {
            int modelDim = 8;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));
            var gamma = Enumerable.Repeat(1.0, modelDim).ToArray();
            var beta = Enumerable.Repeat(0.0, modelDim).ToArray();

            Tensor Sublayer(Tensor x) => x;

            var output = await ResidualSkeleton.ApplyAsync(input, Sublayer, gamma, beta);
            Assert.Equal(input.Shape, output.Shape);
        }

        [Fact]
        public void TestResidualNullInputThrows()
        {
            var gamma = new double[] { 1.0 };
            var beta = new double[] { 0.0 };
            Tensor Sublayer(Tensor x) => x;

            Assert.Throws<ArgumentNullException>(() => ResidualSkeleton.Apply(null!, Sublayer, gamma, beta));
            Assert.Throws<ArgumentNullException>(() => ResidualSkeleton.Apply(Tensor.Random(new TensorShape3D(1, 1, 1)), null!, gamma, beta));
            Assert.Throws<ArgumentNullException>(() => ResidualSkeleton.Apply(Tensor.Random(new TensorShape3D(1, 1, 1)), Sublayer, null!, beta));
            Assert.Throws<ArgumentNullException>(() => ResidualSkeleton.Apply(Tensor.Random(new TensorShape3D(1, 1, 1)), Sublayer, gamma, null!));
        }

        [Fact]
        public void TestEmbeddingLookup()
        {
            int batchSize = 2;
            int seqLen = 3;
            int vocabSize = 10;
            int modelDim = 4;

            var tokenIds = new int[,] { { 1, 2, 3 }, { 4, 5, 6 } };
            var tokenEmbedding = Tensor.Random(new TensorShape3D(1, vocabSize, modelDim));
            var positionEmbedding = Tensor.Random(new TensorShape3D(1, seqLen, modelDim));

            var result = EmbeddingSkeleton.Lookup(tokenIds, tokenEmbedding, positionEmbedding, vocabSize);
            Assert.Equal(new TensorShape3D(batchSize, seqLen, modelDim), result.Shape);
        }

        [Fact]
        public void TestEmbeddingLookupOutOfRange()
        {
            var tokenIds = new int[,] { { 100 } };
            var tokenEmbedding = Tensor.Random(new TensorShape3D(1, 10, 4));
            var positionEmbedding = Tensor.Random(new TensorShape3D(1, 1, 4));

            Assert.Throws<IndexOutOfRangeException>(() =>
                EmbeddingSkeleton.Lookup(tokenIds, tokenEmbedding, positionEmbedding, 10));
        }

        [Fact]
        public void TestEmbeddingNullInputThrows()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 10, 4));
            Assert.Throws<ArgumentNullException>(() => EmbeddingSkeleton.Lookup(null!, valid, valid, 10));
            Assert.Throws<ArgumentNullException>(() => EmbeddingSkeleton.Lookup(new int[,] { { 1 } }, null!, valid, 10));
            Assert.Throws<ArgumentNullException>(() => EmbeddingSkeleton.Lookup(new int[,] { { 1 } }, valid, null!, 10));
        }

        [Fact]
        public void TestSampling()
        {
            var logits = new Tensor(new TensorShape3D(1, 1, 10));
            for (int d = 0; d < 10; d++)
                logits[0, 0, d] = d;

            var rng = new Random(42);

            var greedyResult = SamplingSkeleton.Sample(logits, temperature: 0, rng: rng);
            Assert.Equal(1, greedyResult.GetLength(0));
            Assert.Equal(1, greedyResult.GetLength(1));
            Assert.Equal(9, greedyResult[0, 0]);

            var topKResult = SamplingSkeleton.Sample(logits, temperature: 1.0, topK: 3, rng: rng);
            Assert.Equal(1, topKResult.GetLength(0));
            Assert.Equal(1, topKResult.GetLength(1));
        }

        [Fact]
        public async Task TestSamplingAsync()
        {
            var logits = new Tensor(new TensorShape3D(1, 1, 10));
            for (int d = 0; d < 10; d++)
                logits[0, 0, d] = d;

            var result = await SamplingSkeleton.SampleAsync(logits, temperature: 1.0, rng: new Random(42));
            Assert.Equal(1, result.GetLength(0));
            Assert.Equal(1, result.GetLength(1));
        }

        [Fact]
        public void TestSamplingNullInputThrows()
        {
            Assert.Throws<ArgumentNullException>(() => SamplingSkeleton.Sample(null!));
        }

        [Fact]
        public void TestGatedFFN()
        {
            int modelDim = 8;
            int hiddenDim = 16;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));

            var wGate = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim));
            var wUp = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim));
            var wDown = Tensor.Random(new TensorShape3D(1, hiddenDim, modelDim));

            var output = GatedFFNSkeleton.Compute(input, wGate, wUp, wDown);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), output.Shape);
        }

        [Fact]
        public async Task TestGatedFFNAsync()
        {
            int modelDim = 8;
            int hiddenDim = 16;
            var input = Tensor.Random(new TensorShape3D(1, 4, modelDim));

            var wGate = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim));
            var wUp = Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim));
            var wDown = Tensor.Random(new TensorShape3D(1, hiddenDim, modelDim));

            var output = await GatedFFNSkeleton.ComputeAsync(input, wGate, wUp, wDown);
            Assert.Equal(new TensorShape3D(1, 4, modelDim), output.Shape);
        }

        [Fact]
        public void TestGatedFFNNullInputThrows()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 4, 8));
            Assert.Throws<ArgumentNullException>(() => GatedFFNSkeleton.Compute(null!, valid, valid, valid));
            Assert.Throws<ArgumentNullException>(() => GatedFFNSkeleton.Compute(valid, null!, valid, valid));
            Assert.Throws<ArgumentNullException>(() => GatedFFNSkeleton.Compute(valid, valid, null!, valid));
            Assert.Throws<ArgumentNullException>(() => GatedFFNSkeleton.Compute(valid, valid, valid, null!));
        }

        [Fact]
        public void TestFlashAttention()
        {
            var shape = new TensorShape3D(1, 4, 8);
            var Q = Tensor.Random(shape);
            var K = Tensor.Random(shape);
            var V = Tensor.Random(shape);

            var output = FlashAttentionSkeleton.Compute(Q, K, V, blockSize: 2);
            Assert.Equal(shape, output.Shape);
        }

        [Fact]
        public async Task TestFlashAttentionAsync()
        {
            var shape = new TensorShape3D(1, 4, 8);
            var Q = Tensor.Random(shape);
            var K = Tensor.Random(shape);
            var V = Tensor.Random(shape);

            var output = await FlashAttentionSkeleton.ComputeAsync(Q, K, V, blockSize: 2);
            Assert.Equal(shape, output.Shape);
        }

        [Fact]
        public void TestFlashAttentionNullInputThrows()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 4, 8));
            Assert.Throws<ArgumentNullException>(() => FlashAttentionSkeleton.Compute(null!, valid, valid));
            Assert.Throws<ArgumentNullException>(() => FlashAttentionSkeleton.Compute(valid, null!, valid));
            Assert.Throws<ArgumentNullException>(() => FlashAttentionSkeleton.Compute(valid, valid, null!));
        }

        [Fact]
        public void TestMoE()
        {
            int modelDim = 8;
            int hiddenDim = 16;
            int numExperts = 4;
            int topK = 2;

            var input = Tensor.Random(new TensorShape3D(1, 2, modelDim));
            var routerWeight = Tensor.Random(new TensorShape3D(1, modelDim, numExperts));

            var expertWeights = new (Tensor w1, Tensor w2)[numExperts];
            for (int e = 0; e < numExperts; e++)
            {
                expertWeights[e] = (
                    Tensor.Random(new TensorShape3D(1, modelDim, hiddenDim)),
                    Tensor.Random(new TensorShape3D(1, hiddenDim, modelDim))
                );
            }

            var output = MoESkeleton.Compute(input, routerWeight, expertWeights, numExperts, topK);
            Assert.Equal(new TensorShape3D(1, 2, modelDim), output.Shape);
        }

        [Fact]
        public void TestMoELoadBalancingLoss()
        {
            int numExperts = 4;
            int topK = 2;
            var routerProbs = Tensor.Random(new TensorShape3D(2, 3, numExperts));

            var loss = MoESkeleton.ComputeLoadBalancingLoss(routerProbs, numExperts, topK);
            Assert.True(loss >= 0);
        }

        [Fact]
        public void TestMoENullInputThrows()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 2, 8));
            var experts = new (Tensor, Tensor)[] { (valid, valid) };

            Assert.Throws<ArgumentNullException>(() => MoESkeleton.Compute(null!, valid, experts, 1));
            Assert.Throws<ArgumentNullException>(() => MoESkeleton.Compute(valid, null!, experts, 1));
            Assert.Throws<ArgumentNullException>(() => MoESkeleton.Compute(valid, valid, null!, 1));
        }

        [Fact]
        public void TestMoEInvalidArguments()
        {
            var valid = Tensor.Random(new TensorShape3D(1, 2, 8));
            var experts = new (Tensor, Tensor)[] { (valid, valid) };

            Assert.Throws<ArgumentException>(() => MoESkeleton.Compute(valid, valid, experts, 0, 2));
            Assert.Throws<ArgumentException>(() => MoESkeleton.Compute(valid, valid, experts, 1, 0));
            Assert.Throws<ArgumentException>(() => MoESkeleton.Compute(valid, valid, experts, 1, 2));
        }

        [Fact]
        public void TestBeamSearch()
        {
            int batchSize = 1;
            int maxNewTokens = 3;
            int beamSize = 2;
            int vocabSize = 10;

            var initialTokens = new int[,] { { 1, 2 } };

            double[,] GetLogits(int[,] input)
            {
                int batch = input.GetLength(0);
                int seqLen = input.GetLength(1);
                var logits = new double[batch * seqLen, vocabSize];
                var rng = new Random(42);
                for (int i = 0; i < batch * seqLen; i++)
                    for (int v = 0; v < vocabSize; v++)
                        logits[i, v] = rng.NextDouble();
                return logits;
            }

            var result = BeamSearchSkeleton.Search(
                initialTokens, maxNewTokens, beamSize, vocabSize, GetLogits);

            Assert.Equal(batchSize, result.GetLength(0));
            Assert.Equal(maxNewTokens, result.GetLength(1));
        }

        [Fact]
        public async Task TestBeamSearchAsync()
        {
            int maxNewTokens = 3;
            int beamSize = 2;
            int vocabSize = 10;

            var initialTokens = new int[,] { { 1, 2 } };

            double[,] GetLogits(int[,] input)
            {
                int batch = input.GetLength(0);
                int seqLen = input.GetLength(1);
                var logits = new double[batch * seqLen, vocabSize];
                var rng = new Random(42);
                for (int i = 0; i < batch * seqLen; i++)
                    for (int v = 0; v < vocabSize; v++)
                        logits[i, v] = rng.NextDouble();
                return logits;
            }

            var result = await BeamSearchSkeleton.SearchAsync(
                initialTokens, maxNewTokens, beamSize, vocabSize, GetLogits);

            Assert.Equal(maxNewTokens, result.GetLength(1));
        }

        [Fact]
        public void TestBeamSearchNullInputThrows()
        {
            double[,] GetLogits(int[,] input) => new double[1, 10];

            Assert.Throws<ArgumentNullException>(() => BeamSearchSkeleton.Search(null!, 5, 2, 10, GetLogits));
            Assert.Throws<ArgumentNullException>(() => BeamSearchSkeleton.Search(new int[,] { { 1 } }, 5, 2, 10, null!));
        }
    }
}
