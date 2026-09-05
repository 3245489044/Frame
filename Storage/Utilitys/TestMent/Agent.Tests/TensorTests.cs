namespace Agent.Tests
{
    public class TensorTests
    {
        [Fact]
        public void TestConstructorAndProperties()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = new Tensor(shape);

            Assert.Equal(shape, t.Shape);
            Assert.Equal(2, t.BatchSize);
            Assert.Equal(3, t.SeqLength);
            Assert.Equal(4, t.Dimension);
        }

        [Fact]
        public void TestZeros()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Zeros(shape);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(0.0, t[b, s, d]);
        }

        [Fact]
        public void TestOnes()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Ones(shape);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(1.0, t[b, s, d]);
        }

        [Fact]
        public void TestRandom()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Random(shape);

            Assert.Equal(shape, t.Shape);
            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                    {
                        Assert.InRange(t[b, s, d], 0.0, 1.0);
                    }
        }

        [Fact]
        public void TestRandomWithSeed()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var rng1 = new Random(42);
            var rng2 = new Random(42);

            var t1 = Tensor.Random(shape, rng1);
            var t2 = Tensor.Random(shape, rng2);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(t1[b, s, d], t2[b, s, d]);
        }

        [Fact]
        public void TestFrom2DArray()
        {
            var data = new double[,] { { 1, 2, 3 }, { 4, 5, 6 } };
            var t = new Tensor(data);

            Assert.Equal(new TensorShape3D(1, 2, 3), t.Shape);
            Assert.Equal(1.0, t[0, 0, 0]);
            Assert.Equal(2.0, t[0, 0, 1]);
            Assert.Equal(3.0, t[0, 0, 2]);
            Assert.Equal(4.0, t[0, 1, 0]);
            Assert.Equal(5.0, t[0, 1, 1]);
            Assert.Equal(6.0, t[0, 1, 2]);
        }

        [Fact]
        public void TestIndexerSetAndGet()
        {
            var t = Tensor.Zeros(new TensorShape3D(2, 3, 4));
            t[1, 2, 3] = 42.0;
            Assert.Equal(42.0, t[1, 2, 3]);
        }

        [Fact]
        public void TestAdd()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t1 = Tensor.Ones(shape);
            var t2 = Tensor.Ones(shape);

            var result = t1.Add(t2);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(2.0, result[b, s, d]);
        }

        [Fact]
        public void TestSubtract()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t1 = Tensor.Ones(shape).Multiply(5.0);
            var t2 = Tensor.Ones(shape).Multiply(3.0);

            var result = t1.Subtract(t2);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(2.0, result[b, s, d]);
        }

        [Fact]
        public void TestMultiply()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t1 = Tensor.Ones(shape).Multiply(3.0);
            var t2 = Tensor.Ones(shape).Multiply(4.0);

            var result = t1.Multiply(t2);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(12.0, result[b, s, d]);
        }

        [Fact]
        public void TestDivide()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t1 = Tensor.Ones(shape).Multiply(10.0);
            var t2 = Tensor.Ones(shape).Multiply(2.0);

            var result = t1.Divide(t2);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(5.0, result[b, s, d]);
        }

        [Fact]
        public void TestScalarMultiply()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Ones(shape);

            var result = t.Multiply(3.5);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(3.5, result[b, s, d]);
        }

        [Fact]
        public void TestScalarDivide()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Ones(shape).Multiply(10.0);

            var result = t.Divide(2.0);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(5.0, result[b, s, d]);
        }

        [Fact]
        public void TestScalarAdd()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Ones(shape);

            var result = t.Add(5.0);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(6.0, result[b, s, d]);
        }

        [Fact]
        public void TestMean()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Ones(shape);

            var mean = t.Mean(dim: 2);
            Assert.Equal(new TensorShape3D(2, 3, 1), mean.Shape);
            Assert.Equal(1.0, mean[0, 0, 0]);
        }

        [Fact]
        public void TestSum()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Ones(shape);

            var sum = t.Sum(dim: 2);
            Assert.Equal(new TensorShape3D(2, 3, 1), sum.Shape);
            Assert.Equal(4.0, sum[0, 0, 0]);
        }

        [Fact]
        public void TestMax()
        {
            var shape = new TensorShape3D(1, 2, 3);
            var t = new Tensor(shape);
            t[0, 0, 0] = 1.0; t[0, 0, 1] = 5.0; t[0, 0, 2] = 3.0;
            t[0, 1, 0] = 7.0; t[0, 1, 1] = 2.0; t[0, 1, 2] = 9.0;

            var max = t.Max(dim: 2);
            Assert.Equal(new TensorShape3D(1, 2, 1), max.Shape);
            Assert.Equal(5.0, max[0, 0, 0]);
            Assert.Equal(9.0, max[0, 1, 0]);
        }

        [Fact]
        public void TestArgMax()
        {
            var shape = new TensorShape3D(1, 2, 3);
            var t = new Tensor(shape);
            t[0, 0, 0] = 1.0; t[0, 0, 1] = 5.0; t[0, 0, 2] = 3.0;
            t[0, 1, 0] = 7.0; t[0, 1, 1] = 2.0; t[0, 1, 2] = 9.0;

            var argmax = t.ArgMax(dim: 2);
            Assert.Equal(new TensorShape3D(1, 2, 1), argmax.Shape);
            Assert.Equal(1.0, argmax[0, 0, 0]);
            Assert.Equal(2.0, argmax[0, 1, 0]);
        }

        [Fact]
        public void TestMatMul()
        {
            var matA = new Tensor(new double[,] { { 1, 2, 3 }, { 4, 5, 6 } });
            var matB = new Tensor(new double[,] { { 7, 8 }, { 9, 10 }, { 11, 12 } });

            var result = matA.MatMul(matB);

            Assert.Equal(new TensorShape3D(1, 2, 2), result.Shape);
            Assert.Equal(58.0, result[0, 0, 0], 5);
            Assert.Equal(64.0, result[0, 0, 1], 5);
            Assert.Equal(139.0, result[0, 1, 0], 5);
            Assert.Equal(154.0, result[0, 1, 1], 5);
        }

        [Fact]
        public void TestTranspose()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Random(shape);

            var transposed = t.Transpose(1, 2);
            Assert.Equal(new TensorShape3D(2, 4, 3), transposed.Shape);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(t[b, s, d], transposed[b, d, s]);
        }

        [Fact]
        public void TestSoftmax()
        {
            var shape = new TensorShape3D(1, 1, 4);
            var t = new Tensor(shape);
            t[0, 0, 0] = 1.0; t[0, 0, 1] = 2.0; t[0, 0, 2] = 3.0; t[0, 0, 3] = 4.0;

            var softmax = t.Softmax(dim: 2);

            double sum = 0;
            for (int d = 0; d < 4; d++)
            {
                Assert.InRange(softmax[0, 0, d], 0.0, 1.0);
                sum += softmax[0, 0, d];
            }
            Assert.Equal(1.0, sum, 5);
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
        public void TestActivationFunctions()
        {
            var shape = new TensorShape3D(1, 2, 4);
            var input = Tensor.Random(shape);

            var relu = ActivationFunctions.ReLU(input);
            Assert.Equal(shape, relu.Shape);
            for (int b = 0; b < 1; b++)
                for (int s = 0; s < 2; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.Equal(Math.Max(0, input[b, s, d]), relu[b, s, d]);

            var gelu = ActivationFunctions.GELU(input);
            Assert.Equal(shape, gelu.Shape);

            var swish = ActivationFunctions.Swish(input);
            Assert.Equal(shape, swish.Shape);

            var sigmoid = ActivationFunctions.Sigmoid(input);
            Assert.Equal(shape, sigmoid.Shape);
            for (int b = 0; b < 1; b++)
                for (int s = 0; s < 2; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.InRange(sigmoid[b, s, d], 0.0, 1.0);
        }

        [Fact]
        public async Task TestAsyncOperations()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t1 = Tensor.Random(shape);
            var t2 = Tensor.Random(shape);

            var sum = await Task.Run(() => t1.Add(t2));
            Assert.Equal(shape, sum.Shape);

            var prod = await Task.Run(() => t1.Multiply(t2));
            Assert.Equal(shape, prod.Shape);
        }

        [Fact]
        public void TestDivideByZero()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t1 = Tensor.Ones(shape);
            var t2 = Tensor.Zeros(shape);

            var result = t1.Divide(t2);

            for (int b = 0; b < 2; b++)
                for (int s = 0; s < 3; s++)
                    for (int d = 0; d < 4; d++)
                        Assert.True(double.IsInfinity(result[b, s, d]) ||
                                    double.IsNaN(result[b, s, d]));
        }

        [Fact]
        public void TestShapeMismatchThrows()
        {
            var shape1 = new TensorShape3D(2, 3, 4);
            var shape2 = new TensorShape3D(2, 3, 5);
            var t1 = Tensor.Random(shape1);
            var t2 = Tensor.Random(shape2);

            Assert.ThrowsAny<Exception>(() => t1.Add(t2));
        }

        [Fact]
        public void TestMatMulShapeMismatchThrows()
        {
            var matA = Tensor.Random(new TensorShape3D(2, 3, 4));
            var matB = Tensor.Random(new TensorShape3D(2, 5, 6));

            Assert.ThrowsAny<Exception>(() => matA.MatMul(matB));
        }

        [Fact]
        public void TestNullInputThrows()
        {
            Tensor? nullTensor = null;
            var validTensor = Tensor.Random(new TensorShape3D(1, 1, 1));

            Assert.Throws<ArgumentNullException>(() => validTensor.Add(nullTensor!));
            Assert.Throws<ArgumentNullException>(() => validTensor.Subtract(nullTensor!));
            Assert.Throws<ArgumentNullException>(() => validTensor.Multiply(nullTensor!));
            Assert.Throws<ArgumentNullException>(() => validTensor.Divide(nullTensor!));
        }

        [Fact]
        public void TestDataRef()
        {
            var shape = new TensorShape3D(2, 3, 4);
            var t = Tensor.Random(shape);

            var dataRef = t.DataRef;
            Assert.NotNull(dataRef);
            Assert.Equal(2, dataRef.GetLength(0));
            Assert.Equal(3, dataRef.GetLength(1));
            Assert.Equal(4, dataRef.GetLength(2));
        }
    }
}
