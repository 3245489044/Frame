namespace Agent.Tests
{
    public class HeadTests
    {
        [Fact]
        public void TestRegressionHead()
        {
            int modelDim = 8;
            int batchSize = 2;

            var regHead = new RegressionHead(modelDim, 1);
            regHead.Weight = Tensor.Random(new TensorShape3D(1, modelDim, 1));
            regHead.Bias = Tensor.Zeros(new TensorShape3D(1, 1, 1));

            var features = Tensor.Random(new TensorShape3D(batchSize, 1, modelDim));
            var regOutput = regHead.Forward(features);
            Assert.Equal(new TensorShape3D(batchSize, 1, 1), regOutput.Shape);
        }

        [Fact]
        public void TestRegressionHeadNullInputThrows()
        {
            var head = new RegressionHead(8, 1);
            Assert.Throws<InvalidOperationException>(() => head.Forward(null!));
        }

        [Fact]
        public void TestClassificationHead()
        {
            int modelDim = 8;
            int numClasses = 3;
            int batchSize = 2;

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

            double sum = 0;
            for (int c = 0; c < numClasses; c++)
                sum += probs[0, 0, c];
            Assert.Equal(1.0, sum, 5);
        }

        [Fact]
        public void TestClassificationHeadNullInputThrows()
        {
            var head = new ClassificationHead(8, 3);
            Assert.Throws<InvalidOperationException>(() => head.Forward(null!));
            Assert.Throws<InvalidOperationException>(() => head.ForwardSoftmax(null!));
            Assert.Throws<InvalidOperationException>(() => head.PredictClass(null!));
        }

        [Fact]
        public void TestSequenceOutputHead()
        {
            int modelDim = 8;
            int outputDim = 2;
            int batchSize = 2;
            int seqLen = 4;

            var seqHead = new SequenceOutputHead(modelDim, outputDim);
            seqHead.Weight = Tensor.Random(new TensorShape3D(1, modelDim, outputDim));
            seqHead.Bias = Tensor.Zeros(new TensorShape3D(1, 1, outputDim));

            var seqFeatures = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));
            var seqOutput = seqHead.Forward(seqFeatures);
            Assert.Equal(new TensorShape3D(batchSize, seqLen, outputDim), seqOutput.Shape);
        }

        [Fact]
        public void TestSequenceOutputHeadNullInputThrows()
        {
            var head = new SequenceOutputHead(8, 2);
            Assert.Throws<InvalidOperationException>(() => head.Forward(null!));
        }

        [Fact]
        public void TestLMHead()
        {
            int modelDim = 8;
            int vocabSize = 10;
            int batchSize = 2;
            int seqLen = 3;

            var lmHead = new ClassificationHead(modelDim, vocabSize);
            lmHead.Weight = Tensor.Random(new TensorShape3D(1, modelDim, vocabSize));
            lmHead.Bias = Tensor.Zeros(new TensorShape3D(1, 1, vocabSize));
            var features = Tensor.Random(new TensorShape3D(batchSize, seqLen, modelDim));

            var logits = lmHead.Forward(features);
            Assert.Equal(new TensorShape3D(batchSize, seqLen, vocabSize), logits.Shape);
        }

        [Fact]
        public void TestLMHeadNullInputThrows()
        {
            var head = new ClassificationHead(8, 10);
            Assert.Throws<InvalidOperationException>(() => head.Forward(null!));
        }
    }
}
