#region USINGS
using System;
using System.Collections.Generic;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
using Agent.Transformers.Models;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Runtime
{
    public abstract class BaseFittingStrategy : IFittingStrategy
    {
        public abstract string Name { get; }
        public abstract FittingType FittingType { get; }

        #region 模板方法：Fit

        public virtual bool Fit(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (X == null) throw new ArgumentNullException(nameof(X));
            if (y == null) throw new ArgumentNullException(nameof(y));

            var config = context.Config;
            context.InputDim = X.Dimension;
            context.OutputDim = y.Dimension;
            context.NumSamples = X.BatchSize;

            if (config.AutoNormalize)
            {
                NormalizeInput(context, X);
                NormalizeOutput(context, y);
            }

            InitializeWeights(context);

            bool success = FitWeights(context, X, y, progressCallback);

            context.FinalLoss = ComputeLoss(context, X, y);
            context.IsFitted = success;

            return success;
        }

        protected abstract bool FitWeights(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null);

        #endregion

        #region 模板方法：Predict

        public virtual Tensor Predict(RuntimeContext context, Tensor X)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.IsFitted)
                throw new InvalidOperationException("Model has not been fitted yet.");

            var normalizedX = context.Config.AutoNormalize
                ? ApplyInputNormalization(context, X)
                : X;

            var hidden = ForwardThroughBlocks(context, normalizedX);

            var pooled = PoolFeatures(hidden);

            var rawOutput = pooled.MatMul(context.OutputWeight!);

            var output = context.Config.AutoNormalize
                ? DenormalizeOutput(context, rawOutput)
                : rawOutput;

            return output;
        }

        public virtual double ComputeLoss(RuntimeContext context, Tensor X, Tensor y)
        {
            var predictions = Predict(context, X);
            return ComputeMSE(predictions, y);
        }

        #endregion

        #region 权重初始化

        protected virtual void InitializeWeights(RuntimeContext context)
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
                InitializeBlocks(context);
            }
        }

        protected void InitializeBlocks(RuntimeContext context)
        {
            var config = context.Config;
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

        #endregion

        #region 前向传播与特征提取

        protected Tensor ForwardThroughBlocks(RuntimeContext context, Tensor X)
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

        protected List<Tensor> ForwardWithCache(RuntimeContext context, Tensor X)
        {
            var outputs = new List<Tensor>(context.Config.NumLayers);
            var hidden = X;

            if (context.Blocks != null)
            {
                for (int i = 0; i < context.Blocks.Count; i++)
                {
                    hidden = context.Blocks[i].Forward(hidden);
                    outputs.Add(hidden);
                }
            }

            return outputs;
        }

        protected (List<Tensor> outputs, List<Tensor> attentionWeights) ForwardWithCacheAndWeights(
            RuntimeContext context, Tensor X)
        {
            int numLayers = context.Config.NumLayers;
            var outputs = new List<Tensor>(numLayers);
            var attentionWeights = new List<Tensor>(numLayers);
            var hidden = X;

            if (context.Blocks != null)
            {
                for (int i = 0; i < context.Blocks.Count; i++)
                {
                    hidden = context.Blocks[i].ForwardWithAttentionWeights(
                        hidden, out var attnW);
                    outputs.Add(hidden);
                    attentionWeights.Add(attnW);
                }
            }

            return (outputs, attentionWeights);
        }


        protected Tensor ExtractFeatures(RuntimeContext context, Tensor X)
        {
            var hidden = ForwardThroughBlocks(context, X);
            return PoolFeatures(hidden);
        }

        protected Tensor PoolFeatures(Tensor hidden)
        {
            int batchSize = hidden.BatchSize;
            int seqLen = hidden.SeqLength;
            int modelDim = hidden.Dimension;

            var pooled = new Tensor(new TensorShape3D(batchSize, 1, modelDim));
            for (int b = 0; b < batchSize; b++)
            {
                for (int d = 0; d < modelDim; d++)
                {
                    double sum = 0;
                    for (int s = 0; s < seqLen; s++)
                    {
                        sum += hidden[b, s, d];
                    }
                    pooled[b, 0, d] = sum / seqLen;
                }
            }

            return pooled;
        }

        protected Tensor ExtractSample(Tensor X, int index)
        {
            var sample = new Tensor(new TensorShape3D(1, X.SeqLength, X.Dimension));
            for (int s = 0; s < X.SeqLength; s++)
                for (int d = 0; d < X.Dimension; d++)
                    sample[0, s, d] = X[index, s, d];
            return sample;
        }

        #endregion

        #region 线性代数

        protected Tensor ComputeXtX(Tensor X, double lambda)
        {
            int n = X.BatchSize;
            int d = X.Dimension;

            var result = new Tensor(new TensorShape3D(1, d, d));

            for (int i = 0; i < d; i++)
            {
                for (int j = 0; j < d; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < n; k++)
                    {
                        sum += X[k, 0, i] * X[k, 0, j];
                    }
                    result[0, i, j] = sum;
                }
            }

            for (int i = 0; i < d; i++)
            {
                result[0, i, i] += lambda;
            }

            return result;
        }

        protected Tensor ComputeXty(Tensor X, Tensor y)
        {
            int n = X.BatchSize;
            int d = X.Dimension;
            int outputDim = y.Dimension;

            var result = new Tensor(new TensorShape3D(1, d, outputDim));

            for (int i = 0; i < d; i++)
            {
                for (int j = 0; j < outputDim; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < n; k++)
                    {
                        sum += X[k, 0, i] * y[k, 0, j];
                    }
                    result[0, i, j] = sum;
                }
            }

            return result;
        }

        protected Tensor SolveLinearSystem(Tensor A, Tensor B)
        {
            int n = A.Dimension;
            int m = B.Dimension;

            var augmented = new double[n, n + m];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    augmented[i, j] = A[0, i, j];
                for (int j = 0; j < m; j++)
                    augmented[i, n + j] = B[0, i, j];
            }

            for (int col = 0; col < n; col++)
            {
                int maxRow = col;
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(augmented[row, col]) > Math.Abs(augmented[maxRow, col]))
                        maxRow = row;
                }

                for (int j = col; j < n + m; j++)
                {
                    var temp = augmented[col, j];
                    augmented[col, j] = augmented[maxRow, j];
                    augmented[maxRow, j] = temp;
                }

                for (int row = col + 1; row < n; row++)
                {
                    double factor = augmented[row, col] / augmented[col, col];
                    for (int j = col; j < n + m; j++)
                    {
                        augmented[row, j] -= factor * augmented[col, j];
                    }
                }
            }

            var result = new Tensor(new TensorShape3D(1, n, m));
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = 0; j < m; j++)
                {
                    double sum = augmented[i, n + j];
                    for (int k = i + 1; k < n; k++)
                    {
                        sum -= augmented[i, k] * result[0, k, j];
                    }
                    result[0, i, j] = sum / augmented[i, i];
                }
            }

            return result;
        }

        #endregion

        #region 归一化

        protected void NormalizeInput(RuntimeContext context, Tensor X)
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

        protected void NormalizeOutput(RuntimeContext context, Tensor y)
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

        protected Tensor ApplyInputNormalization(RuntimeContext context, Tensor X)
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

        protected Tensor DenormalizeOutput(RuntimeContext context, Tensor y)
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

        protected double ComputeMSE(Tensor predictions, Tensor targets)
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

        #region 统一训练/推理步（Step）

        public virtual (Tensor output, bool converged) Step(
            RuntimeContext context,
            Tensor X,
            Tensor? y = null,
            double learningRate = 0,
            IUpdateOperator? updater = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (X == null) throw new ArgumentNullException(nameof(X));

            var normalizedX = context.Config.AutoNormalize
                ? ApplyInputNormalization(context, X)
                : X;

            var hidden = ForwardThroughBlocks(context, normalizedX);

            var pooled = PoolFeatures(hidden);

            var rawOutput = pooled.MatMul(context.OutputWeight!);

            var output = context.Config.AutoNormalize
                ? DenormalizeOutput(context, rawOutput)
                : rawOutput;

            if (y != null && learningRate > 0 && context.OutputWeight != null)
            {
                var error = SubtractTensors(rawOutput, y);

                var operator_ = updater ?? new GradientDescentOperator();
                operator_.Update(context, pooled, error, learningRate);

                return (output, false);
            }

            return (output, true);
        }

        #endregion

        #region 张量运算


        protected Tensor AddTensors(Tensor a, Tensor b)
        {
            var result = new Tensor(a.Shape);
            for (int i = 0; i < a.BatchSize; i++)
                for (int j = 0; j < a.SeqLength; j++)
                    for (int k = 0; k < a.Dimension; k++)
                        result[i, j, k] = a[i, j, k] + b[i, j, k];
            return result;
        }

        protected Tensor SubtractTensors(Tensor a, Tensor b)
        {
            var result = new Tensor(a.Shape);
            for (int i = 0; i < a.BatchSize; i++)
                for (int j = 0; j < a.SeqLength; j++)
                    for (int k = 0; k < a.Dimension; k++)
                        result[i, j, k] = a[i, j, k] - b[i, j, k];
            return result;
        }

        protected Tensor ScaleTensor(Tensor t, double scale)
        {
            var result = new Tensor(t.Shape);
            for (int i = 0; i < t.BatchSize; i++)
                for (int j = 0; j < t.SeqLength; j++)
                    for (int k = 0; k < t.Dimension; k++)
                        result[i, j, k] = t[i, j, k] * scale;
            return result;
        }

        #endregion
    }
}
