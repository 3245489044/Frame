#region USINGS
using System;
using System.Buffers;
#endregion

namespace MathematicalAlgorithms
{
    public static class ReduceOperations
    {
        public static double Reduce(double[] input, Func<ReadOnlySpan<double>, double> reducer)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (reducer == null) throw new ArgumentNullException(nameof(reducer));
            if (input.Length == 0) return 0;

            return reducer(new ReadOnlySpan<double>(input));
        }

        public static double[,] Reduce(double[,] input, int dim, Func<ReadOnlySpan<double>, double> reducer)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (reducer == null) throw new ArgumentNullException(nameof(reducer));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);

            if (dim == 1)
            {
                var result = new double[rows, 1];
                double[] values = ArrayPool<double>.Shared.Rent(cols);
                try
                {
                    for (int i = 0; i < rows; i++)
                    {
                        for (int j = 0; j < cols; j++)
                            values[j] = input[i, j];
                        result[i, 0] = reducer(new ReadOnlySpan<double>(values, 0, cols));
                    }
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(values);
                }
                return result;
            }
            else if (dim == 0)
            {
                var result = new double[1, cols];
                double[] values = ArrayPool<double>.Shared.Rent(rows);
                try
                {
                    for (int j = 0; j < cols; j++)
                    {
                        for (int i = 0; i < rows; i++)
                            values[i] = input[i, j];
                        result[0, j] = reducer(new ReadOnlySpan<double>(values, 0, rows));
                    }
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(values);
                }
                return result;
            }
            else
            {
                throw new ArgumentException("Dimension must be 0 or 1", nameof(dim));
            }
        }

        public static double Mean(double[] input)
        {
            return Reduce(input, values =>
            {
                double sum = 0;
                for (int i = 0; i < values.Length; i++)
                    sum += values[i];
                return sum / values.Length;
            });
        }

        public static double[,] Mean(double[,] input, int dim)
        {
            return Reduce(input, dim, values =>
            {
                double sum = 0;
                for (int i = 0; i < values.Length; i++)
                    sum += values[i];
                return sum / values.Length;
            });
        }

        public static double Var(double[] input)
        {
            return Reduce(input, values =>
            {
                double sum = 0, sumSq = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    double val = values[i];
                    sum += val;
                    sumSq += val * val;
                }
                double mean = sum / values.Length;
                return sumSq / values.Length - mean * mean;
            });
        }

        public static double[,] Var(double[,] input, int dim)
        {
            return Reduce(input, dim, values =>
            {
                double sum = 0, sumSq = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    double val = values[i];
                    sum += val;
                    sumSq += val * val;
                }
                double mean = sum / values.Length;
                return sumSq / values.Length - mean * mean;
            });
        }

        public static double Sum(double[] input)
        {
            return Reduce(input, values =>
            {
                double sum = 0;
                for (int i = 0; i < values.Length; i++)
                    sum += values[i];
                return sum;
            });
        }

        public static double[,] Sum(double[,] input, int dim)
        {
            return Reduce(input, dim, values =>
            {
                double sum = 0;
                for (int i = 0; i < values.Length; i++)
                    sum += values[i];
                return sum;
            });
        }

        public static double Max(double[] input)
        {
            return Reduce(input, values =>
            {
                double max = values[0];
                for (int i = 1; i < values.Length; i++)
                    if (values[i] > max) max = values[i];
                return max;
            });
        }

        public static double[,] Max(double[,] input, int dim)
        {
            return Reduce(input, dim, values =>
            {
                double max = values[0];
                for (int i = 1; i < values.Length; i++)
                    if (values[i] > max) max = values[i];
                return max;
            });
        }

        public static int ArgMax(double[] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Length == 0) return -1;

            int maxIdx = 0;
            double maxVal = input[0];
            for (int i = 1; i < input.Length; i++)
            {
                if (input[i] > maxVal)
                {
                    maxVal = input[i];
                    maxIdx = i;
                }
            }
            return maxIdx;
        }

        public static double[,] ArgMax(double[,] input, int dim)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);

            if (dim == 1)
            {
                var result = new double[rows, 1];
                for (int i = 0; i < rows; i++)
                {
                    int maxIdx = 0;
                    double maxVal = input[i, 0];
                    for (int j = 1; j < cols; j++)
                    {
                        if (input[i, j] > maxVal)
                        {
                            maxVal = input[i, j];
                            maxIdx = j;
                        }
                    }
                    result[i, 0] = maxIdx;
                }
                return result;
            }
            else if (dim == 0)
            {
                var result = new double[1, cols];
                for (int j = 0; j < cols; j++)
                {
                    int maxIdx = 0;
                    double maxVal = input[0, j];
                    for (int i = 1; i < rows; i++)
                    {
                        if (input[i, j] > maxVal)
                        {
                            maxVal = input[i, j];
                            maxIdx = i;
                        }
                    }
                    result[0, j] = maxIdx;
                }
                return result;
            }
            else
            {
                throw new ArgumentException("Dimension must be 0 or 1", nameof(dim));
            }
        }
    }
}
