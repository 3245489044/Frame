#region USINGS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using MathematicalAlgorithms;
#endregion

namespace Agent.Transformers.Core
{
    public class Tensor
    {
        private readonly double[,,] _data;

        private static readonly LoopEngine _loopEngine = new();

        public TensorShape3D Shape { get; }

        public int BatchSize => Shape.Batch;

        public int SeqLength => Shape.SeqLen;

        public int Dimension => Shape.Dim;

        #region 构造函数

        public Tensor(TensorShape3D shape)
        {
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
            _data = new double[shape.Batch, shape.SeqLen, shape.Dim];
        }

        public Tensor(double[,,] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            _data = data;
            Shape = new TensorShape3D(
                data.GetLength(0),
                data.GetLength(1),
                data.GetLength(2));
        }

        public Tensor(double[,] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            int rows = data.GetLength(0);
            int cols = data.GetLength(1);
            _data = new double[1, rows, cols];
            Shape = new TensorShape3D(1, rows, cols);

            _loopEngine.ExecuteInPlaceSync(
                (Src: data, Dst: _data, Cols: cols),
                (ctx, b, s) => {
                    for (int d = 0; d < ctx.Cols; d++)
                        ctx.Dst[0, s, d] = ctx.Src[s, d];
                },
                new LoopShape(1, rows));
        }

        internal Tensor(double[,,] data, TensorShape3D shape)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        }

        #endregion

        #region 索引器

        public double this[int b, int s, int d]
        {
            get
            {
                Shape.ValidateIndex(b, s, d);
                return _data[b, s, d];
            }
            set
            {
                Shape.ValidateIndex(b, s, d);
                _data[b, s, d] = value;
            }
        }

        public double[] GetRow(int b, int s)
        {
            Shape.ValidateIndex(b, s, 0);
            var row = new double[Dimension];
            for (int d = 0; d < Dimension; d++)
                row[d] = _data[b, s, d];
            return row;
        }

        public void SetRow(int b, int s, double[] values)
        {
            Shape.ValidateIndex(b, s, 0);
            if (values == null || values.Length != Dimension)
                throw new ArgumentException($"Row must have length {Dimension}", nameof(values));

            for (int d = 0; d < Dimension; d++)
                _data[b, s, d] = values[d];
        }

        #endregion

        #region 3D↔2D 转换（公共静态方法，供其他组件复用）

        public double[,] To2D()
        {
            int rows = BatchSize * SeqLength;
            int cols = Dimension;
            var result = new double[rows, cols];

            _loopEngine.ExecuteInPlaceSync(
                (Src: _data, Dst: result, Cols: cols, SeqLen: SeqLength),
                (ctx, b, s) => {
                    int row = b * ctx.SeqLen + s;
                    for (int d = 0; d < ctx.Cols; d++)
                        ctx.Dst[row, d] = ctx.Src[b, s, d];
                },
                new LoopShape(BatchSize, SeqLength));

            return result;
        }

        public static Tensor From2D(double[,] data2D, int batchSize, int seqLen)
        {
            int rows = data2D.GetLength(0);
            int cols = data2D.GetLength(1);
            var result = new double[batchSize, seqLen, cols];

            var engine = new LoopEngine();
            engine.ExecuteInPlaceSync(
                (Src: data2D, Dst: result, Cols: cols, SeqLen: seqLen),
                (ctx, b, s) => {
                    int row = b * ctx.SeqLen + s;
                    for (int d = 0; d < ctx.Cols; d++)
                        ctx.Dst[b, s, d] = ctx.Src[row, d];
                },
                new LoopShape(batchSize, seqLen));

            return new Tensor(result, new TensorShape3D(batchSize, seqLen, cols));
        }

        #endregion

        #region 工厂方法

        public static Tensor FromArray(double[,] data) => new Tensor(data);

        public static Tensor FromArray(double[,,] data) => new Tensor(data);

        public static Tensor FromMatrix(Matrix<double> matrix)
        {
            if (matrix == null)
                throw new ArgumentNullException(nameof(matrix));

            var data = new double[1, matrix.RowCount, matrix.ColumnCount];
            var engine = new LoopEngine();
            engine.ExecuteInPlaceSync(
                (Src: matrix, Dst: data, Cols: matrix.ColumnCount),
                (ctx, b, i) => {
                    for (int j = 0; j < ctx.Cols; j++)
                        ctx.Dst[0, i, j] = ctx.Src[i, j];
                },
                new LoopShape(1, matrix.RowCount));

            return new Tensor(data, new TensorShape3D(1, matrix.RowCount, matrix.ColumnCount));
        }

        public static Tensor Zeros(TensorShape3D shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            return new Tensor(shape);
        }

        public static Tensor Ones(TensorShape3D shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            var data2D = TensorFactory.Ones(shape.Batch * shape.SeqLen, shape.Dim);
            return From2D(data2D, shape.Batch, shape.SeqLen);
        }

        public static Tensor Fill(TensorShape3D shape, double value)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            var data2D = TensorFactory.Fill(shape.Batch * shape.SeqLen, shape.Dim, value);
            return From2D(data2D, shape.Batch, shape.SeqLen);
        }

        public static Tensor Random(TensorShape3D shape, Random? rng = null)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            rng ??= new Random(42);
            var data2D = TensorFactory.Random(shape.Batch * shape.SeqLen, shape.Dim, rng);
            return From2D(data2D, shape.Batch, shape.SeqLen);
        }

        #endregion

        #region 与 Matrix 互转

        public Matrix<double> ToMatrix()
        {
            var rows = BatchSize * SeqLength;
            var matrix = Matrix<double>.Build.Dense(rows, Dimension);

            _loopEngine.ExecuteInPlaceSync(
                (Src: _data, Dst: matrix, BatchSize: BatchSize, SeqLength: SeqLength, Dim: Dimension),
                (ctx, b, s) => {
                    for (int d = 0; d < ctx.Dim; d++)
                        ctx.Dst[b * ctx.SeqLength + s, d] = ctx.Src[b, s, d];
                },
                new LoopShape(BatchSize, SeqLength));

            return matrix;
        }

        public double[,,] ToArray()
        {
            var copy = new double[BatchSize, SeqLength, Dimension];
            Buffer.BlockCopy(_data, 0, copy, 0, _data.Length * sizeof(double));
            return copy;
        }

        #endregion

        #region 辅助方法（消除 3D↔2D 转换模板冗余）

        private Tensor ApplyUnary(Func<double[,], double[,]> op)
        {
            var a2D = To2D();
            var result2D = op(a2D);
            return From2D(result2D, BatchSize, SeqLength);
        }

        private Tensor ApplyBinary(Tensor other, Func<double[,], double[,], double[,]> op)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var resultShape = TensorShape3D.Broadcast(Shape, other.Shape);

            var a2D = To2D();
            var b2D = other.To2D();
            var result2D = op(a2D, b2D);

            return From2D(result2D, resultShape.Batch, resultShape.SeqLen);
        }

        #endregion

        #region 逐元素运算（委托给 BroadcastOperations）

        public Tensor Add(Tensor other) => ApplyBinary(other, BroadcastOperations.Add);

        public Tensor Subtract(Tensor other) => ApplyBinary(other, BroadcastOperations.Subtract);

        public Tensor Multiply(Tensor other) => ApplyBinary(other, BroadcastOperations.Multiply);

        public Tensor Divide(Tensor other) => ApplyBinary(other, BroadcastOperations.Divide);

        public Tensor Multiply(double scalar) => ApplyUnary(a2D => UnaryOperations.MultiplyScalar(a2D, scalar));

        public Tensor Divide(double scalar)
        {
            if (scalar == 0)
                throw new DivideByZeroException("Cannot divide by zero");
            return ApplyUnary(a2D => UnaryOperations.DivideScalar(a2D, scalar));
        }

        public Tensor Sqrt() => ApplyUnary(UnaryOperations.Sqrt);

        public Tensor Exp() => ApplyUnary(UnaryOperations.Exp);

        public Tensor Add(double scalar) => ApplyUnary(a2D => UnaryOperations.AddScalar(a2D, scalar));

        #endregion

        #region 异步运算（消除 Task.Run 模板冗余）

        private Task<Tensor> RunAsync(Func<Tensor> operation, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            }, cancellationToken);
        }

        private Task<Tensor> RunAsync<T>(T arg, Func<T, Tensor> operation, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return operation(arg);
            }, cancellationToken);
        }

        public Task<Tensor> AddAsync(Tensor other,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
            => RunAsync(other, Add, cancellationToken);

        public Task<Tensor> MultiplyAsync(Tensor other,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
            => RunAsync(other, Multiply, cancellationToken);

        #endregion

        #region 归约运算（委托给 ReduceOperations）

        private Tensor Reduce(int dim, bool keepDim, Func<ReadOnlySpan<double>, double> reducer)
        {
            DimensionHelper.ValidateDim(dim, 3);

            var a2D = To2D();

            int reduceDim = dim == 2 ? 1 : 0;
            var result2D = ReduceOperations.Reduce(a2D, reduceDim, reducer);

            int newBatch = dim == 0 ? 1 : BatchSize;
            int newSeqLen = dim == 1 ? 1 : SeqLength;
            int newDim = dim == 2 ? 1 : Dimension;

            return From2D(result2D, newBatch, newSeqLen);
        }

        public Tensor Mean(int dim, bool keepDim = false)
        {
            return Reduce(dim, keepDim, values => ReduceOperations.Mean(values.ToArray()));
        }

        public Tensor Var(int dim, bool keepDim = false)
        {
            return Reduce(dim, keepDim, values => ReduceOperations.Var(values.ToArray()));
        }

        public Tensor Sum(int dim, bool keepDim = false)
        {
            return Reduce(dim, keepDim, values => ReduceOperations.Sum(values.ToArray()));
        }

        public Tensor Max(int dim, bool keepDim = false)
        {
            return Reduce(dim, keepDim, values => ReduceOperations.Max(values.ToArray()));
        }

        public Tensor ArgMax(int dim)
        {
            return Reduce(dim, false, values => (double)ReduceOperations.ArgMax(values.ToArray()));
        }

        #endregion


        #region 矩阵运算

        public Tensor MatMul(Tensor other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Dimension != other.SeqLength)
                throw new InvalidOperationException(
                    $"Cannot matmul: this.Dim={Dimension} != other.SeqLen={other.SeqLength}");

            int resultBatch = Math.Max(BatchSize, other.BatchSize);
            int resultSeqLen = SeqLength;
            int resultDim = other.Dimension;

            var result = new Tensor(new TensorShape3D(resultBatch, resultSeqLen, resultDim));

            _loopEngine.ExecuteInPlaceSync(
                (A: _data, B: other._data, R: result._data,
                 BatchA: BatchSize, BatchB: other.BatchSize,
                 K: Dimension),
                (ctx, bIdx, i) => {
                    int srcB1 = Math.Min(bIdx, ctx.BatchA - 1);
                    int srcB2 = Math.Min(bIdx, ctx.BatchB - 1);
                    for (int j = 0; j < ctx.R.GetLength(2); j++)
                    {
                        double sum = 0;
                        for (int k = 0; k < ctx.K; k++)
                            sum += ctx.A[srcB1, i, k] * ctx.B[srcB2, k, j];
                        ctx.R[bIdx, i, j] = sum;
                    }
                },
                new LoopShape(resultBatch, resultSeqLen));

            return result;
        }

        public Task<Tensor> MatMulAsync(Tensor other,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
            => RunAsync(other, MatMul, cancellationToken);

        public async Task<Tensor> MatMulBlockedAsync(Tensor other,
            int blockSize = 256,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Dimension != other.SeqLength)
                throw new InvalidOperationException(
                    $"Cannot matmul: this.Dim={Dimension} != other.SeqLen={other.SeqLength}");

            int resultBatch = Math.Max(BatchSize, other.BatchSize);
            int resultSeqLen = SeqLength;
            int resultDim = other.Dimension;

            var result = new Tensor(new TensorShape3D(resultBatch, resultSeqLen, resultDim));

            await _loopEngine.ExecuteBlockedAsync(
                (A: _data, B: other._data, R: result._data,
                 BatchA: BatchSize, BatchB: other.BatchSize,
                 K: Dimension),
                (ctx, bIdx, i) => {
                    int srcB1 = Math.Min(bIdx, ctx.BatchA - 1);
                    int srcB2 = Math.Min(bIdx, ctx.BatchB - 1);
                    for (int j = 0; j < ctx.R.GetLength(2); j++)
                    {
                        double sum = 0;
                        for (int k = 0; k < ctx.K; k++)
                            sum += ctx.A[srcB1, i, k] * ctx.B[srcB2, k, j];
                        ctx.R[bIdx, i, j] = sum;
                    }
                },
                new LoopShape(resultBatch, resultSeqLen),
                blockSize,
                progress: progress,
                cancellationToken: cancellationToken);

            return result;
        }


        public Tensor Transpose(int dim1 = -2, int dim2 = -1)
        {
            dim1 = DimensionHelper.NormalizeDim(dim1, 3);
            dim2 = DimensionHelper.NormalizeDim(dim2, 3);

            if (dim1 == dim2)
                return Clone();

            var shapeArr = new[] { BatchSize, SeqLength, Dimension };
            (shapeArr[dim1], shapeArr[dim2]) = (shapeArr[dim2], shapeArr[dim1]);
            var resultShape = new TensorShape3D(shapeArr[0], shapeArr[1], shapeArr[2]);
            var result = new Tensor(resultShape);

            _loopEngine.ExecuteInPlaceSync(
                (Src: _data, Dst: result._data, Dim1: dim1, Dim2: dim2,
                 BatchSize: BatchSize, SeqLength: SeqLength, Dimension: Dimension),
                (ctx, b, s) => {
                    for (int d = 0; d < ctx.Dimension; d++)
                    {
                        var indices = new[] { b, s, d };
                        var targetIndices = new[] { b, s, d };
                        (targetIndices[ctx.Dim1], targetIndices[ctx.Dim2]) = (indices[ctx.Dim2], indices[ctx.Dim1]);

                        ctx.Dst[
                            targetIndices[0],
                            targetIndices[1],
                            targetIndices[2]] = ctx.Src[b, s, d];
                    }
                },
                new LoopShape(BatchSize, SeqLength));

            return result;
        }

        public Tensor Slice(int batch, int start, int length)
        {
            if (batch < 0 || batch >= BatchSize)
                throw new IndexOutOfRangeException($"Batch {batch} out of range [0, {BatchSize})");
            if (start < 0 || start + length > SeqLength)
                throw new IndexOutOfRangeException(
                    $"Slice [{start}, {start + length}) out of range [0, {SeqLength})");

            var result = new Tensor(new TensorShape3D(1, length, Dimension));

            _loopEngine.ExecuteInPlaceSync(
                (Src: _data, Dst: result._data, Batch: batch, Start: start, Dim: Dimension),
                (ctx, b, s) => {
                    for (int d = 0; d < ctx.Dim; d++)
                        ctx.Dst[0, s, d] = ctx.Src[ctx.Batch, ctx.Start + s, d];
                },
                new LoopShape(1, length));

            return result;
        }

        public Tensor Reshape(int b, int s, int d)
        {
            long newTotal = (long)b * s * d;
            if (newTotal != Shape.TotalElements)
                throw new InvalidOperationException(
                    $"Cannot reshape {Shape} to [{b}×{s}×{d}]: element count mismatch " +
                    $"({Shape.TotalElements} vs {newTotal})");

            var flat = new double[Shape.TotalElements];
            int idx = 0;
            for (int bi = 0; bi < BatchSize; bi++)
                for (int si = 0; si < SeqLength; si++)
                    for (int di = 0; di < Dimension; di++)
                        flat[idx++] = _data[bi, si, di];

            var result = new Tensor(new TensorShape3D(b, s, d));
            idx = 0;
            for (int bi = 0; bi < b; bi++)
                for (int si = 0; si < s; si++)
                    for (int di = 0; di < d; di++)
                        result._data[bi, si, di] = flat[idx++];

            return result;
        }

        public Tensor Softmax(int dim = -1)
        {
            if (this == null)
                throw new ArgumentNullException(nameof(Tensor));

            dim = DimensionHelper.NormalizeDim(dim, 3);

            if (dim != 2)
                throw new ArgumentException("Softmax currently only supports dim=2 (last dimension)", nameof(dim));

            var a2D = To2D();
            var result2D = SoftmaxCalculator.Compute(a2D, dim: 1);
            return From2D(result2D, BatchSize, SeqLength);
        }

        #region 归一化运算（委托给 NormalizationOperations）

        public Tensor LayerNorm(double[] gamma, double[] beta, double eps = 1e-5)
        {
            if (gamma == null) throw new ArgumentNullException(nameof(gamma));
            if (beta == null) throw new ArgumentNullException(nameof(beta));

            var input2D = To2D();
            var result2D = NormalizationOperations.LayerNorm(input2D, gamma, beta, eps);
            return From2D(result2D, BatchSize, SeqLength);
        }

        public Tensor RMSNorm(double[] gamma, double eps = 1e-5)
        {
            if (gamma == null) throw new ArgumentNullException(nameof(gamma));

            var input2D = To2D();
            var result2D = NormalizationOperations.RMSNorm(input2D, gamma, eps);
            return From2D(result2D, BatchSize, SeqLength);
        }

        #endregion

        #region 激活函数运算（委托给 UnaryOperations）

        public Tensor ApplyActivation(Func<double, double> activation)
        {
            if (activation == null) throw new ArgumentNullException(nameof(activation));

            var input2D = To2D();
            var result2D = UnaryOperations.Apply(input2D, activation);
            return From2D(result2D, BatchSize, SeqLength);
        }

        #endregion

        public Tensor Clone()
        {
            var copy = new double[BatchSize, SeqLength, Dimension];
            Buffer.BlockCopy(_data, 0, copy, 0, _data.Length * sizeof(double));
            return new Tensor(copy, Shape);
        }

        public double[,,] DataRef => _data;

        public override string ToString() => $"Tensor{Shape}";

        #endregion
    }
}


