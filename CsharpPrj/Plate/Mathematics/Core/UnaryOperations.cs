#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class UnaryOperations
    {
        private static readonly LoopEngine _engine = new();

        #region 一元运算（逐元素）

        public static double[] Apply(double[] input, Func<double, double> func)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (func == null) throw new ArgumentNullException(nameof(func));

            var result = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
                result[i] = func(input[i]);
            return result;
        }

        public static double[,] Apply(double[,] input, Func<double, double> func)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (func == null) throw new ArgumentNullException(nameof(func));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);

            var result = _engine.ExecuteSync(
                (Input: input, Func: func),
                (data, i, j) => data.Func(data.Input[i, j]),
                new LoopShape(rows, cols));

            var result2D = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static async Task<double[]> ApplyAsync(
            double[] input,
            Func<double, double> func,
            CancellationToken cancellationToken = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (func == null) throw new ArgumentNullException(nameof(func));

            return await Task.Run(() =>
            {
                var result = new double[input.Length];
                for (int i = 0; i < input.Length; i++)
                    result[i] = func(input[i]);
                return result;
            }, cancellationToken);
        }

        #endregion

        #region 标量运算

        public static double[] AddScalar(double[] input, double scalar)
        {
            return Apply(input, x => x + scalar);
        }

        public static double[,] AddScalar(double[,] input, double scalar)
        {
            return Apply(input, x => x + scalar);
        }

        public static double[] MultiplyScalar(double[] input, double scalar)
        {
            return Apply(input, x => x * scalar);
        }

        public static double[,] MultiplyScalar(double[,] input, double scalar)
        {
            return Apply(input, x => x * scalar);
        }

        public static double[] DivideScalar(double[] input, double scalar)
        {
            if (scalar == 0) throw new DivideByZeroException("Cannot divide by zero");
            return Apply(input, x => x / scalar);
        }

        public static double[,] DivideScalar(double[,] input, double scalar)
        {
            if (scalar == 0) throw new DivideByZeroException("Cannot divide by zero");
            return Apply(input, x => x / scalar);
        }

        public static double[] SubtractScalar(double[] input, double scalar)
        {
            return Apply(input, x => x - scalar);
        }

        public static double[,] SubtractScalar(double[,] input, double scalar)
        {
            return Apply(input, x => x - scalar);
        }

        #endregion

        #region 常用数学运算

        public static double[] Abs(double[] input) => Apply(input, Math.Abs);

        public static double[,] Abs(double[,] input) => Apply(input, Math.Abs);

        public static double[] Square(double[] input) => Apply(input, x => x * x);

        public static double[,] Square(double[,] input) => Apply(input, x => x * x);

        public static double[] Sqrt(double[] input) => Apply(input, Math.Sqrt);

        public static double[,] Sqrt(double[,] input) => Apply(input, Math.Sqrt);

        public static double[] Exp(double[] input) => Apply(input, Math.Exp);

        public static double[,] Exp(double[,] input) => Apply(input, Math.Exp);

        public static double[] Log(double[] input) => Apply(input, Math.Log);

        public static double[,] Log(double[,] input) => Apply(input, Math.Log);

        #endregion
    }
}
