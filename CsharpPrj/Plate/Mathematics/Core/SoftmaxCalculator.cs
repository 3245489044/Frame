#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public static class SoftmaxCalculator
    {
        public static double[] Compute(double[] input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (input.Length == 0)
                return Array.Empty<double>();

            int len = input.Length;
            var result = new double[len];

            double maxVal = input[0];
            for (int i = 1; i < len; i++)
                if (input[i] > maxVal)
                    maxVal = input[i];

            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double expVal = Math.Exp(input[i] - maxVal);
                result[i] = expVal;
                sum += expVal;
            }

            if (sum > 0)
            {
                for (int i = 0; i < len; i++)
                    result[i] /= sum;
            }

            return result;
        }

        public static double[,] Compute(double[,] input, int dim = 1)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);
            var result = new double[rows, cols];

            if (dim == 1)
            {
                for (int i = 0; i < rows; i++)
                {
                    var row = new double[cols];
                    for (int j = 0; j < cols; j++)
                        row[j] = input[i, j];

                    var softmaxRow = Compute(row);

                    for (int j = 0; j < cols; j++)
                        result[i, j] = softmaxRow[j];
                }
            }
            else if (dim == 0)
            {
                for (int j = 0; j < cols; j++)
                {
                    var col = new double[rows];
                    for (int i = 0; i < rows; i++)
                        col[i] = input[i, j];

                    var softmaxCol = Compute(col);

                    for (int i = 0; i < rows; i++)
                        result[i, j] = softmaxCol[i];
                }
            }
            else
            {
                throw new ArgumentException("Dimension must be 0 or 1", nameof(dim));
            }

            return result;
        }

        public static double[,] ComputeSoftmax2D(double[,] input) => Compute(input, dim: 1);

        public static double[,,] Compute(double[,,] input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            int batchSize = input.GetLength(0);
            int seqLen = input.GetLength(1);
            int dim = input.GetLength(2);

            var result = new double[batchSize, seqLen, dim];

            for (int b = 0; b < batchSize; b++)
            {
                for (int i = 0; i < seqLen; i++)
                {
                    double maxVal = input[b, i, 0];
                    for (int j = 1; j < dim; j++)
                    {
                        if (input[b, i, j] > maxVal)
                            maxVal = input[b, i, j];
                    }

                    double sum = 0;
                    for (int j = 0; j < dim; j++)
                    {
                        double expVal = Math.Exp(input[b, i, j] - maxVal);
                        result[b, i, j] = expVal;
                        sum += expVal;
                    }

                    if (sum > 0)
                    {
                        for (int j = 0; j < dim; j++)
                            result[b, i, j] /= sum;
                    }
                }
            }

            return result;
        }

        public static double[] LogSoftmax(double[] input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (input.Length == 0)
                return Array.Empty<double>();

            int len = input.Length;
            var result = new double[len];

            double maxVal = input[0];
            for (int i = 1; i < len; i++)
                if (input[i] > maxVal)
                    maxVal = input[i];

            double sum = 0;
            for (int i = 0; i < len; i++)
                sum += Math.Exp(input[i] - maxVal);
            double logSum = Math.Log(sum);

            for (int i = 0; i < len; i++)
                result[i] = input[i] - maxVal - logSum;

            return result;
        }
    }
}
