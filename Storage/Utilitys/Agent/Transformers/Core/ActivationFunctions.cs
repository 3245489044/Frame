#region USINGS
using System;
using MathematicalAlgorithms;
#endregion

namespace Agent.Transformers.Core
{
    public static class ActivationFunctions
    {
        #region 标量激活函数（委托给 Mathematics）

        public static double ReLU(double x) => MathematicalAlgorithms.ActivationFunctions.ReLU(x);

        public static double GELU(double x) => MathematicalAlgorithms.ActivationFunctions.GELU(x);

        public static double Swish(double x) => MathematicalAlgorithms.ActivationFunctions.Swish(x);

        public static double Sigmoid(double x) => MathematicalAlgorithms.ActivationFunctions.Sigmoid(x);

        public static double SiLU(double x) => MathematicalAlgorithms.ActivationFunctions.Swish(x);

        public static double Apply(double x, MathematicalAlgorithms.ActivationType type)
            => MathematicalAlgorithms.ActivationFunctions.Apply(x, type);

        public static string GetName(MathematicalAlgorithms.ActivationType type)
            => MathematicalAlgorithms.ActivationFunctions.GetName(type);

        public static bool IsValidActivation(double value, MathematicalAlgorithms.ActivationType type)
            => MathematicalAlgorithms.ActivationFunctions.IsValidActivation(value, type);

        #endregion

        #region 张量激活函数（委托给 Tensor.ApplyActivation）

        public static Tensor ReLU(Tensor input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.ApplyActivation(x => ReLU(x));
        }

        public static Tensor GELU(Tensor input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.ApplyActivation(x => GELU(x));
        }

        public static Tensor Swish(Tensor input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.ApplyActivation(x => Swish(x));
        }

        public static Tensor Sigmoid(Tensor input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.ApplyActivation(x => Sigmoid(x));
        }

        public static Tensor SiLU(Tensor input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.ApplyActivation(x => SiLU(x));
        }

        public static Tensor Apply(Tensor input, MathematicalAlgorithms.ActivationType type)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.ApplyActivation(x => Apply(x, type));
        }

        #endregion

    }
}
