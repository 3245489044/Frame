#region USINGS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
using Agent.Transformers.Runtime;
#endregion

namespace Agent.UnifiedRuntime
{
    public class UnifiedRuntimeEngineSkeleton : IDisposable
    {
        #region 属性

        public RuntimeContext Context { get; }

        public bool IsFitted => Context.IsFitted;

        public IUpdateOperator DefaultUpdater { get; set; }

        public double DefaultLearningRate { get; set; } = 0.01;

        public MatrixComputationConfig MatrixConfig { get; }

        private readonly MatrixComputationFlow _flow;

        #endregion

        #region 构造函数

        public UnifiedRuntimeEngineSkeleton(
            RuntimeConfig config,
            IUpdateOperator? defaultUpdater = null,
            MatrixComputationConfig? matrixConfig = null)
        {
            Context = new RuntimeContext(config ?? throw new ArgumentNullException(nameof(config)));
            DefaultUpdater = defaultUpdater ?? new GradientDescentOperator();
            MatrixConfig = matrixConfig ?? MatrixComputationConfig.Default;
            _flow = new MatrixComputationFlow(MatrixConfig);
        }

        #endregion

        #region 统一训练/推理步（Step）

        public (Tensor output, bool converged) Step(
            Tensor X,
            Tensor? y = null,
            double? learningRate = null,
            IUpdateOperator? updater = null)
        {
            if (X == null) throw new ArgumentNullException(nameof(X));

            double lr = learningRate ?? DefaultLearningRate;
            var operator_ = updater ?? DefaultUpdater;

            var normalizedX = Context.Config.AutoNormalize
                ? ApplyInputNormalization(Context, X)
                : X;

            var hidden = ForwardThroughBlocks(Context, normalizedX);

            var pooled = PoolFeatures(hidden);

            var rawOutput = pooled.MatMul(Context.OutputWeight!);

            var output = Context.Config.AutoNormalize
                ? DenormalizeOutput(Context, rawOutput)
                : rawOutput;

            if (y != null && lr > 0 && Context.OutputWeight != null)
            {
                var error = SubtractTensors(rawOutput, y);

                operator_.Update(Context, pooled, error, lr);

                return (output, false);
            }

            return (output, true);
        }

        public void Fit(
            Tensor X,
            Tensor y,
            int? maxIterations = null,
            double? learningRate = null,
            IUpdateOperator? updater = null,
            Action<int, int, double>? progressCallback = null)
        {
            var sw = Stopwatch.StartNew();
            Context.Reset();

            Context.InputDim = X.Dimension;
            Context.OutputDim = y.Dimension;
            Context.NumSamples = X.BatchSize;

            if (Context.Config.AutoNormalize)
            {
                NormalizeInput(Context, X);
                NormalizeOutput(Context, y);
            }

            InitializeWeights(Context);

            int maxIter = maxIterations ?? Context.Config.MaxIterations;
            double lr = learningRate ?? DefaultLearningRate;
            var operator_ = updater ?? DefaultUpdater;

            for (int iter = 0; iter < maxIter; iter++)
            {
                var (output, _) = Step(X, y, lr, operator_);

                double loss = ComputeMSE(output, y);
                Context.LossHistory.Add(loss);
                progressCallback?.Invoke(iter, maxIter, loss);

                if (iter > 0 && Math.Abs(loss - Context.LossHistory[^2]) < Context.Config.ConvergenceThreshold)
                    break;
            }

            sw.Stop();
            Context.FittingTimeMs = sw.ElapsedMilliseconds;
            Context.FinalLoss = Context.LossHistory.Count > 0 ? Context.LossHistory[^1] : 0;
            Context.IsFitted = true;
        }

        public Tensor Predict(Tensor X)
        {
            EnsureFitted();
            var (output, _) = Step(X, learningRate: 0);
            return output;
        }

        public double Evaluate(Tensor X, Tensor y)
        {
            EnsureFitted();
            var predictions = Predict(X);
            return ComputeMSE(predictions, y);
        }

        #endregion

        #region 前向传播与特征提取

        private Tensor ForwardThroughBlocks(RuntimeContext context, Tensor X)
        {
            var hidden = X;
            if (context.Blocks != null)
            {
                for (int i = 0; i < context.Blocks.Count; i++)
                {
                    hidden = context.Blocks[i].Forward(hidden);
                }
            }
            return hidden;
        }

        private Tensor PoolFeatures(Tensor hidden)
        {
            int batchSize = hidden.BatchSize;
            int seqLen = hidden.SeqLength;
            int modelDim = hidden.Dimension;

            var pooled = new Tensor(new TensorShape3D(batchSize, 1, modelDim));

            for (int b = 0; b < batchSize; b++)
            {
                var sampleData = new double[seqLen, modelDim];
                for (int s = 0; s < seqLen; s++)
                    for (int d = 0; d < modelDim; d++)
                        sampleData[s, d] = hidden[b, s, d];

                var shape = new MatrixShape(1, modelDim);
                var meanTask = MatrixEngine.ComputeAsync(
                    (Data: sampleData, SeqLen: seqLen),
                    (ctx, _, d) =>
                    {
                        double sum = 0;
                        for (int s = 0; s < ctx.SeqLen; s++)
                            sum += ctx.Data[s, d];
                        return sum / ctx.SeqLen;
                    },
                    shape,
                    MatrixConfig,
                    progress: null,
                    CancellationToken.None);

                var meanMatrix = meanTask.GetAwaiter().GetResult();
                for (int d = 0; d < modelDim; d++)
                    pooled[b, 0, d] = meanMatrix[0, d];
            }

            return pooled;
        }

        #endregion

        #region 权重初始化

        private void InitializeWeights(RuntimeContext context)
        {
            var config = context.Config;

            if (context.OutputWeight == null)
            {
                int modelDim = config.ModelDim;
                int outputDim = context.OutputDim;
                context.OutputWeight = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            if (context.Blocks == null || context.Blocks.Count != config.NumLayers)
            {
                context.Blocks = new List<TransformerBlock>(config.NumLayers);
                var rng = new Random(42);

                for (int i = 0; i < config.NumLayers; i++)
                {
                    context.Blocks.Add(new TransformerBlock(
                        config.ModelDim,
                        config.NumHeads,
                        config.FfnHiddenDim,
                        config.Activation,
                        rng));
                }
            }
        }

        #endregion

        #region 归一化

        private void NormalizeInput(RuntimeContext context, Tensor X)
        {
            int numSamples = X.BatchSize;
            int seqLen = X.SeqLength;
            int inputDim = X.Dimension;

            var mean = new double[inputDim];
            var std = new double[inputDim];

            var meanShape = new MatrixShape(1, inputDim);
            var meanTask = MatrixEngine.ComputeAsync(
                (X, NumSamples: numSamples, SeqLen: seqLen),
                (ctx, _, d) =>
                {
                    double sum = 0;
                    int count = 0;
                    for (int b = 0; b < ctx.NumSamples; b++)
                        for (int s = 0; s < ctx.SeqLen; s++)
                        {
                            sum += ctx.X[b, s, d];
                            count++;
                        }
                    return sum / count;
                },
                meanShape,
                MatrixConfig,
                progress: null,
                CancellationToken.None);

            var meanMatrix = meanTask.GetAwaiter().GetResult();
            for (int d = 0; d < inputDim; d++)
                mean[d] = meanMatrix[0, d];

            var stdTask = MatrixEngine.ComputeAsync(
                (X, Mean: mean, NumSamples: numSamples, SeqLen: seqLen),
                (ctx, _, d) =>
                {
                    double sumSq = 0;
                    int count = 0;
                    for (int b = 0; b < ctx.NumSamples; b++)
                        for (int s = 0; s < ctx.SeqLen; s++)
                        {
                            double diff = ctx.X[b, s, d] - ctx.Mean[d];
                            sumSq += diff * diff;
                            count++;
                        }
                    return Math.Sqrt(sumSq / count) + 1e-8;
                },
                meanShape,
                MatrixConfig,
                progress: null,
                CancellationToken.None);

            var stdMatrix = stdTask.GetAwaiter().GetResult();
            for (int d = 0; d < inputDim; d++)
                std[d] = stdMatrix[0, d];

            context.InputMean = mean;
            context.InputStd = std;
        }

        private void NormalizeOutput(RuntimeContext context, Tensor y)
        {
            int numSamples = y.BatchSize;
            int outputDim = y.Dimension;

            var mean = new double[outputDim];
            var std = new double[outputDim];

            var shape = new MatrixShape(1, outputDim);
            var meanTask = MatrixEngine.ComputeAsync(
                (Y: y, NumSamples: numSamples),
                (ctx, _, d) =>
                {
                    double sum = 0;
                    for (int b = 0; b < ctx.NumSamples; b++)
                        sum += ctx.Y[b, 0, d];
                    return sum / ctx.NumSamples;
                },
                shape,
                MatrixConfig,
                progress: null,
                CancellationToken.None);

            var meanMatrix = meanTask.GetAwaiter().GetResult();
            for (int d = 0; d < outputDim; d++)
                mean[d] = meanMatrix[0, d];

            var stdTask = MatrixEngine.ComputeAsync(
                (Y: y, Mean: mean, NumSamples: numSamples),
                (ctx, _, d) =>
                {
                    double sumSq = 0;
                    for (int b = 0; b < ctx.NumSamples; b++)
                    {
                        double diff = ctx.Y[b, 0, d] - ctx.Mean[d];
                        sumSq += diff * diff;
                    }
                    return Math.Sqrt(sumSq / ctx.NumSamples) + 1e-8;
                },
                shape,
                MatrixConfig,
                progress: null,
                CancellationToken.None);

            var stdMatrix = stdTask.GetAwaiter().GetResult();
            for (int d = 0; d < outputDim; d++)
                std[d] = stdMatrix[0, d];

            context.OutputMean = mean;
            context.OutputStd = std;
        }

        private Tensor ApplyInputNormalization(RuntimeContext context, Tensor X)
        {
            var mean = context.InputMean;
            var std = context.InputStd;
            if (mean == null || std == null) return X;

            var result = new Tensor(X.Shape);
            for (int b = 0; b < X.BatchSize; b++)
                for (int s = 0; s < X.SeqLength; s++)
                    for (int d = 0; d < X.Dimension; d++)
                        result[b, s, d] = (X[b, s, d] - mean[d]) / std[d];
            return result;
        }

        private Tensor DenormalizeOutput(RuntimeContext context, Tensor y)
        {
            var mean = context.OutputMean;
            var std = context.OutputStd;
            if (mean == null || std == null) return y;

            var result = new Tensor(y.Shape);
            for (int b = 0; b < y.BatchSize; b++)
                for (int d = 0; d < y.Dimension; d++)
                    result[b, 0, d] = y[b, 0, d] * std[d] + mean[d];
            return result;
        }

        #endregion

        #region 损失计算

        private double ComputeMSE(Tensor predictions, Tensor targets)
        {
            int n = predictions.BatchSize;
            int outputDim = predictions.Dimension;

            var shape = new MatrixShape(1, 1);
            var task = MatrixEngine.ComputeAsync(
                (Predictions: predictions, Targets: targets, N: n, OutputDim: outputDim),
                (ctx, _, _) =>
                {
                    double sum = 0;
                    for (int i = 0; i < ctx.N; i++)
                        for (int j = 0; j < ctx.OutputDim; j++)
                        {
                            double diff = ctx.Predictions[i, 0, j] - ctx.Targets[i, 0, j];
                            sum += diff * diff;
                        }
                    return sum / (ctx.N * ctx.OutputDim);
                },
                shape,
                MatrixConfig,
                progress: null,
                CancellationToken.None);

            return task.GetAwaiter().GetResult()[0, 0];
        }

        #endregion

        #region 张量运算

        private Tensor SubtractTensors(Tensor a, Tensor b)
        {
            var result = new Tensor(a.Shape);

            var shape = new MatrixShape(a.BatchSize * a.SeqLength, a.Dimension);
            var task = MatrixEngine.ComputeAsync(
                (A: a, B: b),
                (ctx, i, j) =>
                {
                    int batchIdx = i / a.SeqLength;
                    int seqIdx = i % a.SeqLength;
                    return ctx.A[batchIdx, seqIdx, j] - ctx.B[batchIdx, seqIdx, j];
                },
                shape,
                MatrixConfig,
                progress: null,
                CancellationToken.None);

            var matrix = task.GetAwaiter().GetResult();
            for (int i = 0; i < a.BatchSize; i++)
                for (int j = 0; j < a.SeqLength; j++)
                    for (int k = 0; k < a.Dimension; k++)
                        result[i, j, k] = matrix[i * a.SeqLength + j, k];

            return result;
        }

        #endregion

        #region 序列化

        public void Save(string filePath)
        {
            if (!IsFitted)
                throw new InvalidOperationException("Cannot save unfitted model.");

            var modelData = new UnifiedModelSaveData
            {
                Config = Context.Config,
                InputDim = Context.InputDim,
                OutputDim = Context.OutputDim,
                InputMean = Context.InputMean,
                InputStd = Context.InputStd,
                OutputMean = Context.OutputMean,
                OutputStd = Context.OutputStd,
                FinalLoss = Context.FinalLoss,
                NumSamples = Context.NumSamples,
                FittingTimeMs = Context.FittingTimeMs,
                LossHistory = Context.LossHistory.ToArray()
            };

            if (Context.OutputWeight != null)
            {
                int rows = Context.OutputWeight.SeqLength;
                int cols = Context.OutputWeight.Dimension;
                modelData.OutputWeight = new double[rows, cols];
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < cols; j++)
                        modelData.OutputWeight[i, j] = Context.OutputWeight[0, i, j];
            }

            var json = JsonSerializer.Serialize(modelData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(filePath, json);
        }

        public void Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Model file not found.", filePath);

            var json = File.ReadAllText(filePath);
            var modelData = JsonSerializer.Deserialize<UnifiedModelSaveData>(json);

            if (modelData == null)
                throw new InvalidDataException("Failed to deserialize model data.");

            Context.Reset();
            Context.InputDim = modelData.InputDim;
            Context.OutputDim = modelData.OutputDim;
            Context.InputMean = modelData.InputMean;
            Context.InputStd = modelData.InputStd;
            Context.OutputMean = modelData.OutputMean;
            Context.OutputStd = modelData.OutputStd;
            Context.FinalLoss = modelData.FinalLoss;
            Context.NumSamples = modelData.NumSamples;
            Context.FittingTimeMs = modelData.FittingTimeMs;

            if (modelData.LossHistory != null)
                Context.LossHistory.AddRange(modelData.LossHistory);

            if (modelData.OutputWeight != null)
            {
                int rows = modelData.OutputWeight.GetLength(0);
                int cols = modelData.OutputWeight.GetLength(1);
                Context.OutputWeight = new Tensor(new TensorShape3D(1, rows, cols));
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < cols; j++)
                        Context.OutputWeight[0, i, j] = modelData.OutputWeight[i, j];
            }

            Context.IsFitted = true;
        }

        #endregion

        #region 辅助方法

        private void EnsureFitted()
        {
            if (!Context.IsFitted)
                throw new InvalidOperationException(
                    "Model has not been fitted yet. Call Fit() first.");
        }

        public string GetSummary()
        {
            return Context.GetSummary();
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _flow.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    internal class UnifiedModelSaveData
    {
        public RuntimeConfig? Config { get; set; }
        public int InputDim { get; set; }
        public int OutputDim { get; set; }
        public double[]? InputMean { get; set; }
        public double[]? InputStd { get; set; }
        public double[]? OutputMean { get; set; }
        public double[]? OutputStd { get; set; }
        public double[,]? OutputWeight { get; set; }
        public double FinalLoss { get; set; }
        public int NumSamples { get; set; }
        public long FittingTimeMs { get; set; }
        public double[]? LossHistory { get; set; }
    }
}
