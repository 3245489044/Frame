namespace Agent.Tests
{
    public class DataTests
    {
        [Fact]
        public void TestFeatureExtractorInitialize()
        {
            int modelDim = 8;
            int numLayers = 2;
            int numHeads = 2;
            int ffnHiddenDim = 32;

            var extractor = new FeatureExtractor(modelDim);
            extractor.Initialize(numLayers, numHeads, ffnHiddenDim, ActivationType.GELU);
            Assert.True(extractor.IsInitialized);
        }

        [Fact]
        public void TestFeatureExtractorMeanPooling()
        {
            int modelDim = 8;
            int numLayers = 2;
            int numHeads = 2;
            int ffnHiddenDim = 32;

            var extractor = new FeatureExtractor(modelDim);
            extractor.Initialize(numLayers, numHeads, ffnHiddenDim, ActivationType.GELU);

            var input = Tensor.Random(new TensorShape3D(2, 4, modelDim));

            extractor.Pooling = PoolingStrategy.Mean;
            var meanFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), meanFeatures.Shape);
        }

        [Fact]
        public void TestFeatureExtractorMaxPooling()
        {
            int modelDim = 8;
            int numLayers = 2;
            int numHeads = 2;
            int ffnHiddenDim = 32;

            var extractor = new FeatureExtractor(modelDim);
            extractor.Initialize(numLayers, numHeads, ffnHiddenDim, ActivationType.GELU);

            var input = Tensor.Random(new TensorShape3D(2, 4, modelDim));

            extractor.Pooling = PoolingStrategy.Max;
            var maxFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), maxFeatures.Shape);
        }

        [Fact]
        public void TestFeatureExtractorFirstPooling()
        {
            int modelDim = 8;
            int numLayers = 2;
            int numHeads = 2;
            int ffnHiddenDim = 32;

            var extractor = new FeatureExtractor(modelDim);
            extractor.Initialize(numLayers, numHeads, ffnHiddenDim, ActivationType.GELU);

            var input = Tensor.Random(new TensorShape3D(2, 4, modelDim));

            extractor.Pooling = PoolingStrategy.First;
            var firstFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), firstFeatures.Shape);
        }

        [Fact]
        public void TestFeatureExtractorLastPooling()
        {
            int modelDim = 8;
            int numLayers = 2;
            int numHeads = 2;
            int ffnHiddenDim = 32;

            var extractor = new FeatureExtractor(modelDim);
            extractor.Initialize(numLayers, numHeads, ffnHiddenDim, ActivationType.GELU);

            var input = Tensor.Random(new TensorShape3D(2, 4, modelDim));

            extractor.Pooling = PoolingStrategy.Last;
            var lastFeatures = extractor.Extract(input);
            Assert.Equal(new TensorShape3D(2, 1, modelDim), lastFeatures.Shape);
        }

        [Fact]
        public void TestFeatureExtractorNotInitializedThrows()
        {
            var extractor = new FeatureExtractor(8);
            var input = Tensor.Random(new TensorShape3D(2, 4, 8));

            Assert.Throws<InvalidOperationException>(() => extractor.Extract(input));
        }

        [Fact]
        public void TestFeatureExtractorNullInputThrows()
        {
            var extractor = new FeatureExtractor(8);
            extractor.Initialize(2, 2, 32, ActivationType.GELU);

            Assert.Throws<ArgumentNullException>(() => extractor.Extract(null!));
        }

        [Fact]
        public void TestDataEncoderFit()
        {
            var encoder = new DataEncoder();

            var labels = new[] { "cat", "dog", "bird", "cat", "dog" };
            encoder.Fit(labels);
            Assert.Equal(3, encoder.NumClasses);
            Assert.True(encoder.IsFitted);
        }

        [Fact]
        public void TestDataEncoderOneHot()
        {
            var encoder = new DataEncoder();

            var labels = new[] { "cat", "dog", "bird", "cat", "dog" };
            encoder.Fit(labels);

            var testLabels = new[] { "cat", "dog", "bird" };
            var oneHot = encoder.EncodeOneHot(testLabels);
            Assert.Equal(new TensorShape3D(3, 1, 3), oneHot.Shape);
            Assert.Equal(1.0, oneHot[0, 0, 0]);
            Assert.Equal(1.0, oneHot[1, 0, 1]);
            Assert.Equal(1.0, oneHot[2, 0, 2]);
        }

        [Fact]
        public void TestDataEncoderLabelEncode()
        {
            var encoder = new DataEncoder();

            var labels = new[] { "cat", "dog", "bird", "cat", "dog" };
            encoder.Fit(labels);

            var testLabels = new[] { "cat", "dog", "bird" };
            var encoded = encoder.EncodeLabel(testLabels);
            Assert.Equal(3, encoded.Length);
            Assert.Equal(0, encoded[0]);
            Assert.Equal(1, encoded[1]);
            Assert.Equal(2, encoded[2]);
        }

        [Fact]
        public void TestDataEncoderDecode()
        {
            var encoder = new DataEncoder();

            var labels = new[] { "cat", "dog", "bird" };
            encoder.Fit(labels);

            var encoded = new int[] { 0, 1, 2 };
            var decoded = encoder.Decode(encoded);
            Assert.Equal(labels, decoded);
        }

        [Fact]
        public void TestDataEncoderNotFittedThrows()
        {
            var encoder = new DataEncoder();

            Assert.Throws<InvalidOperationException>(() => encoder.EncodeOneHot(new[] { "cat" }));
            Assert.Throws<InvalidOperationException>(() => encoder.EncodeLabel(new[] { "cat" }));
            Assert.Throws<InvalidOperationException>(() => encoder.Decode(new[] { 0 }));
        }

        [Fact]
        public void TestDataEncoderNullInputThrows()
        {
            var encoder = new DataEncoder();
            Assert.Throws<ArgumentNullException>(() => encoder.Fit(null!));
            Assert.Throws<InvalidOperationException>(() => encoder.EncodeOneHot(null!));
            Assert.Throws<InvalidOperationException>(() => encoder.EncodeLabel(null!));
            Assert.Throws<InvalidOperationException>(() => encoder.Decode(null!));
        }
    }
}
