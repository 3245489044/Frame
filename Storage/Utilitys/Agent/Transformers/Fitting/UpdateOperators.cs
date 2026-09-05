#region USINGS
using System;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class GradientDescentOperator : IUpdateOperator
    {
        public string Name => "GradientDescent";

        public void Update(RuntimeContext context, Tensor features, Tensor error, double learningRate)
        {
            if (context.OutputWeight == null)
            {
                int modelDim = context.Config.ModelDim;
                int outputDim = context.OutputDim;
                context.OutputWeight = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            for (int r = 0; r < context.Config.ModelDim; r++)
            {
                for (int c = 0; c < context.OutputDim; c++)
                {
                    double grad = features[0, 0, r] * error[0, 0, c];
                    context.OutputWeight[0, r, c] -= learningRate * grad;
                }
            }
        }
    }

    public class MomentumOperator : IUpdateOperator
    {
        public string Name => "Momentum";

        public double MomentumCoeff { get; set; } = 0.9;

        private Tensor? _velocity;

        public void Update(RuntimeContext context, Tensor features, Tensor error, double learningRate)
        {
            int modelDim = context.Config.ModelDim;
            int outputDim = context.OutputDim;

            if (context.OutputWeight == null)
            {
                context.OutputWeight = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            if (_velocity == null)
            {
                _velocity = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            for (int r = 0; r < modelDim; r++)
            {
                for (int c = 0; c < outputDim; c++)
                {
                    double grad = features[0, 0, r] * error[0, 0, c];
                    _velocity[0, r, c] = MomentumCoeff * _velocity[0, r, c] + (1 - MomentumCoeff) * grad;
                    context.OutputWeight[0, r, c] -= learningRate * _velocity[0, r, c];
                }
            }
        }
    }

    public class LeastSquaresOperator : IUpdateOperator
    {
        public string Name => "LeastSquares";

        public double Lambda { get; set; } = 0.01;

        public void Update(RuntimeContext context, Tensor features, Tensor error, double learningRate)
        {
            var config = context.Config;

            var prediction = features.MatMul(context.OutputWeight!);
            var y = new Tensor(error.Shape);
            for (int i = 0; i < error.BatchSize; i++)
                for (int j = 0; j < error.Dimension; j++)
                    y[i, 0, j] = prediction[i, 0, j] + error[i, 0, j];

            var XtX = ComputeXtX(features, Lambda);
            var Xty = ComputeXty(features, y);
            var W = SolveLinearSystem(XtX, Xty);

            context.OutputWeight = W;
        }

        private Tensor ComputeXtX(Tensor X, double lambda)
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

        private Tensor ComputeXty(Tensor X, Tensor y)
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

        private Tensor SolveLinearSystem(Tensor A, Tensor B)
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
    }

    public class IdentityOperator : IUpdateOperator
    {
        public string Name => "Identity";

        public void Update(RuntimeContext context, Tensor features, Tensor error, double learningRate)
        {
        }
    }

    public class PolicyGradientOperator : IUpdateOperator
    {
        public string Name => "PolicyGradient";

        public double EntropyCoeff { get; set; } = 0.01;

        public void Update(RuntimeContext context, Tensor features, Tensor error, double learningRate)
        {
            if (context.OutputWeight == null)
            {
                int modelDim = context.Config.ModelDim;
                int outputDim = context.OutputDim;
                context.OutputWeight = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            for (int r = 0; r < context.Config.ModelDim; r++)
            {
                for (int c = 0; c < context.OutputDim; c++)
                {
                    double grad = features[0, 0, r] * error[0, 0, c];
                    context.OutputWeight[0, r, c] += learningRate * grad;
                }
            }

            if (context.ValueWeight != null && context.ValueBias != null)
            {
                for (int r = 0; r < context.Config.ModelDim; r++)
                {
                    double tdGrad = features[0, 0, r] * error[0, 0, 0];
                    context.ValueWeight[0, r, 0] += learningRate * 0.5 * tdGrad;
                }
                context.ValueBias[0, 0, 0] += learningRate * 0.5 * error[0, 0, 0];
            }
        }
    }

    public class PPOClipOperator : IUpdateOperator
    {
        public string Name => "PPOClip";

        public double ClipEpsilon { get; set; } = 0.2;

        public double EntropyCoeff { get; set; } = 0.01;

        public void Update(RuntimeContext context, Tensor features, Tensor error, double learningRate)
        {
            if (context.OutputWeight == null)
            {
                int modelDim = context.Config.ModelDim;
                int outputDim = context.OutputDim;
                context.OutputWeight = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            double advantage = error[0, 0, 0];

            double clippedAdvantage = Math.Clamp(advantage, -ClipEpsilon, ClipEpsilon);

            for (int r = 0; r < context.Config.ModelDim; r++)
            {
                for (int c = 0; c < context.OutputDim; c++)
                {
                    double grad = features[0, 0, r] * clippedAdvantage;
                    context.OutputWeight[0, r, c] += learningRate * grad;
                }
            }

            if (context.ValueWeight != null && context.ValueBias != null)
            {
                for (int r = 0; r < context.Config.ModelDim; r++)
                {
                    double tdGrad = features[0, 0, r] * error[0, 0, 0];
                    context.ValueWeight[0, r, 0] += learningRate * 0.5 * tdGrad;
                }
                context.ValueBias[0, 0, 0] += learningRate * 0.5 * error[0, 0, 0];
            }
        }
    }
}
