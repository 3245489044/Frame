#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public enum ActivationType
    {
        ReLU,
        GELU,
        Swish,
        Softmax
    }

    public static class ActivationFunctions
    {
        #region 标量激活函数

        public static double ReLU(double x) => Math.Max(0, x);

        public static double GELU(double x)
        {
            const double sqrt2OverPi = 0.7978845608028654;
            return 0.5 * x * (1.0 + Math.Tanh(sqrt2OverPi * (x + 0.044715 * x * x * x)));
        }

        public static double Swish(double x) => x * Sigmoid(x);

        public static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

        public static double Tanh(double x) => Math.Tanh(x);

        public static double Softplus(double x) => Math.Log(1.0 + Math.Exp(x));

        public static double PReLU(double x, double alpha) => x < 0 ? alpha * x : x;

        public static double ELU(double x, double alpha) => x < 0 ? alpha * (Math.Exp(x) - 1.0) : x;

        public static double LeakyReLU(double x, double alpha) => x < 0 ? alpha * x : x;

        public static double Apply(double x, ActivationType type)
        {
            return type switch
            {
                ActivationType.ReLU => ReLU(x),
                ActivationType.GELU => GELU(x),
                ActivationType.Swish => Swish(x),
                _ => throw new ArgumentException($"Unknown activation type: {type}", nameof(type))
            };
        }

        #endregion

        #region 辅助方法

        public static string GetName(ActivationType type)
        {
            return type switch
            {
                ActivationType.ReLU => "ReLU",
                ActivationType.GELU => "GELU",
                ActivationType.Swish => "Swish",
                ActivationType.Softmax => "Softmax",
                _ => "Unknown"
            };
        }

        public static bool IsValidActivation(double value, ActivationType type)
        {
            return type switch
            {
                ActivationType.ReLU => value >= 0 || double.IsNaN(value),
                ActivationType.GELU => !double.IsNaN(value) && !double.IsInfinity(value),
                ActivationType.Swish => !double.IsNaN(value) && !double.IsInfinity(value),
                _ => !double.IsNaN(value)
            };
        }

        #endregion
    }
}
