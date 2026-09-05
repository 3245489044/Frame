#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class TensorFactory
    {
        public static double[,] Zeros(int rows, int cols)
        {
            if (rows <= 0) throw new ArgumentException("Rows must be positive", nameof(rows));
            if (cols <= 0) throw new ArgumentException("Cols must be positive", nameof(cols));

            return new double[rows, cols];
        }

        public static double[,] Ones(int rows, int cols)
        {
            if (rows <= 0) throw new ArgumentException("Rows must be positive", nameof(rows));
            if (cols <= 0) throw new ArgumentException("Cols must be positive", nameof(cols));

            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = 1.0;
            return result;
        }

        public static double[,] Fill(int rows, int cols, double value)
        {
            if (rows <= 0) throw new ArgumentException("Rows must be positive", nameof(rows));
            if (cols <= 0) throw new ArgumentException("Cols must be positive", nameof(cols));

            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = value;
            return result;
        }

        public static double[,] Random(int rows, int cols, Random? rng = null)
        {
            if (rows <= 0) throw new ArgumentException("Rows must be positive", nameof(rows));
            if (cols <= 0) throw new ArgumentException("Cols must be positive", nameof(cols));

            rng ??= new Random();
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = rng.NextDouble();
            return result;
        }

        public static double[,] Identity(int size)
        {
            if (size <= 0) throw new ArgumentException("Size must be positive", nameof(size));

            var result = new double[size, size];
            for (int i = 0; i < size; i++)
                result[i, i] = 1.0;
            return result;
        }

        public static double[,] Diagonal(double[] diagonal)
        {
            if (diagonal == null) throw new ArgumentNullException(nameof(diagonal));

            int size = diagonal.Length;
            var result = new double[size, size];
            for (int i = 0; i < size; i++)
                result[i, i] = diagonal[i];
            return result;
        }

        public static double[,] Create<TState>(
            int rows,
            int cols,
            TState state,
            Func<TState, int, int, double> valueProvider)
        {
            if (rows <= 0) throw new ArgumentException("Rows must be positive", nameof(rows));
            if (cols <= 0) throw new ArgumentException("Cols must be positive", nameof(cols));
            if (valueProvider == null) throw new ArgumentNullException(nameof(valueProvider));

            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = valueProvider(state, i, j);
            return result;
        }

        public static async Task<double[,]> CreateAsync<TState>(
            int rows,
            int cols,
            TState state,
            Func<TState, int, int, double> valueProvider,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Create(rows, cols, state, valueProvider);
            }, cancellationToken);
        }

        public static double[,] FromArray(double[] data, int rows, int cols)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (rows <= 0) throw new ArgumentException("Rows must be positive", nameof(rows));
            if (cols <= 0) throw new ArgumentException("Cols must be positive", nameof(cols));
            if (data.Length != rows * cols)
                throw new ArgumentException(
                    $"Data length ({data.Length}) must equal rows×cols ({rows}×{cols})",
                    nameof(data));

            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = data[i * cols + j];
            return result;
        }

        public static double[,] FromJaggedArray(double[][] jaggedArray)
        {
            if (jaggedArray == null) throw new ArgumentNullException(nameof(jaggedArray));
            if (jaggedArray.Length == 0) return new double[0, 0];

            int rows = jaggedArray.Length;
            int cols = jaggedArray[0].Length;

            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = jaggedArray[i][j];
            return result;
        }
    }
}
