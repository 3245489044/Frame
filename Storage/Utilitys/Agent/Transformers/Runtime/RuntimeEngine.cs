#region USINGS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
using Agent.Transformers.Models;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Runtime
{
    public class RuntimeEngine
    {
        #region 属性

        public RuntimeContext Context { get; }

        public IFittingStrategy Strategy { get; private set; }

        public bool IsFitted => Context.IsFitted;

        public IUpdateOperator DefaultUpdater { get; set; }

        public double DefaultLearningRate { get; set; } = 0.01;

        #endregion

        #region 构造函数

        public RuntimeEngine(
            RuntimeConfig config,
            IFittingStrategy? strategy = null,
            IUpdateOperator? defaultUpdater = null)
        {
            Context = new RuntimeContext(config ?? throw new ArgumentNullException(nameof(config)));
            Strategy = strategy ?? CreateDefaultStrategy(config);
            DefaultUpdater = defaultUpdater ?? new GradientDescentOperator();
        }

        #endregion

        #region 拟合

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

        public (Tensor output, bool converged) Step(
            Tensor X,
            Tensor? y = null,
            double? learningRate = null,
            IUpdateOperator? updater = null)
        {
            if (X == null) throw new ArgumentNullException(nameof(X));

            double lr = learningRate ?? DefaultLearningRate;
            var operator_ = updater ?? DefaultUpdater;

            if (Context.IsFitted || lr > 0)
            {
                return Strategy.Step(Context, X, y, lr, operator_);
            }

            throw new InvalidOperationException(
                "Model has not been fitted yet. Call Fit() first or provide learningRate > 0 for training.");
        }


        public void Fit(double[,] X, double[,] y,
            Action<int, int, double>? progressCallback = null)
        {
            int numSamples = X.GetLength(0);
            int inputDim = X.GetLength(1);
            int outputDim = y.GetLength(1);

            var tensorX = new Tensor(new TensorShape3D(numSamples, 1, inputDim));
            var tensorY = new Tensor(new TensorShape3D(numSamples, 1, outputDim));

            for (int i = 0; i < numSamples; i++)
            {
                for (int j = 0; j < inputDim; j++)
                    tensorX[i, 0, j] = X[i, j];
                for (int j = 0; j < outputDim; j++)
                    tensorY[i, 0, j] = y[i, j];
            }

            Fit(tensorX, tensorY, progressCallback: progressCallback);
        }

        #endregion

        #region 预测

        public Tensor Predict(Tensor X)
        {
            EnsureFitted();
            return Strategy.Predict(Context, X);
        }

        public double[,] Predict(double[,] X)
        {
            EnsureFitted();

            int numSamples = X.GetLength(0);
            int inputDim = X.GetLength(1);

            var tensorX = new Tensor(new TensorShape3D(numSamples, 1, inputDim));
            for (int i = 0; i < numSamples; i++)
                for (int j = 0; j < inputDim; j++)
                    tensorX[i, 0, j] = X[i, j];

            var result = Predict(tensorX);

            int outputDim = result.Dimension;
            var output = new double[numSamples, outputDim];
            for (int i = 0; i < numSamples; i++)
                for (int j = 0; j < outputDim; j++)
                    output[i, j] = result[i, 0, j];

            return output;
        }

        public double[] PredictOne(double[] x)
        {
            EnsureFitted();

            int inputDim = x.Length;
            var tensorX = new Tensor(new TensorShape3D(1, 1, inputDim));
            for (int i = 0; i < inputDim; i++)
                tensorX[0, 0, i] = x[i];

            var result = Predict(tensorX);

            int outputDim = result.Dimension;
            var output = new double[outputDim];
            for (int i = 0; i < outputDim; i++)
                output[i] = result[0, 0, i];

            return output;
        }

        #endregion

        #region 评估

        public double Evaluate(Tensor X, Tensor y)
        {
            EnsureFitted();
            return Strategy.ComputeLoss(Context, X, y);
        }

        public string GetSummary()
        {
            return Context.GetSummary();
        }

        #endregion

        #region 策略管理

        public void SetStrategy(IFittingStrategy strategy)
        {
            Strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        }

        private static IFittingStrategy CreateDefaultStrategy(RuntimeConfig config)
        {
            return config.FittingStrategy switch
            {
                FittingType.LeastSquares => new LeastSquaresFitting(),
                FittingType.GradientFree => new GradientFreeFitting(),
                FittingType.Online => new OnlineFitting(),
                FittingType.LayerwiseBP => new LayerwiseFitting(),
                FittingType.OnlineBackprop => new OnlineBackpropFitting(),
                FittingType.PolicyGradient => new PolicyGradientFitting(),
                FittingType.PPO => new PPOFitting(),
                _ => new LeastSquaresFitting()
            };
        }

        #endregion

        #region 归一化

        private static void NormalizeInput(RuntimeContext context, Tensor X)
        {
            int numSamples = X.BatchSize;
            int seqLen = X.SeqLength;
            int inputDim = X.Dimension;

            var mean = new double[inputDim];
            var std = new double[inputDim];

            for (int d = 0; d < inputDim; d++)
            {
                double sum = 0;
                int count = 0;
                for (int b = 0; b < numSamples; b++)
                    for (int s = 0; s < seqLen; s++)
                    {
                        sum += X[b, s, d];
                        count++;
                    }
                mean[d] = sum / count;
            }

            for (int d = 0; d < inputDim; d++)
            {
                double sumSq = 0;
                int count = 0;
                for (int b = 0; b < numSamples; b++)
                    for (int s = 0; s < seqLen; s++)
                    {
                        double diff = X[b, s, d] - mean[d];
                        sumSq += diff * diff;
                        count++;
                    }
                std[d] = Math.Sqrt(sumSq / count) + 1e-8;
            }

            context.InputMean = mean;
            context.InputStd = std;
        }

        private static void NormalizeOutput(RuntimeContext context, Tensor y)
        {
            int numSamples = y.BatchSize;
            int outputDim = y.Dimension;

            var mean = new double[outputDim];
            var std = new double[outputDim];

            for (int d = 0; d < outputDim; d++)
            {
                double sum = 0;
                for (int b = 0; b < numSamples; b++)
                    sum += y[b, 0, d];
                mean[d] = sum / numSamples;
            }

            for (int d = 0; d < outputDim; d++)
            {
                double sumSq = 0;
                for (int b = 0; b < numSamples; b++)
                {
                    double diff = y[b, 0, d] - mean[d];
                    sumSq += diff * diff;
                }
                std[d] = Math.Sqrt(sumSq / numSamples) + 1e-8;
            }

            context.OutputMean = mean;
            context.OutputStd = std;
        }

        #endregion

        #region 权重初始化

        private static void InitializeWeights(RuntimeContext context)
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

        #region 损失计算

        private static double ComputeMSE(Tensor predictions, Tensor targets)
        {
            int n = predictions.BatchSize;
            int outputDim = predictions.Dimension;

            double sum = 0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < outputDim; j++)
                {
                    double diff = predictions[i, 0, j] - targets[i, 0, j];
                    sum += diff * diff;
                }
            return sum / (n * outputDim);
        }

        #endregion

        #region 序列化

        public void Save(string filePath)
        {
            if (!IsFitted)
                throw new InvalidOperationException("Cannot save unfitted model.");

            var modelData = new ModelSaveData
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
            var modelData = JsonSerializer.Deserialize<ModelSaveData>(json);

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

        #endregion
    }

    internal class ModelSaveData
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
