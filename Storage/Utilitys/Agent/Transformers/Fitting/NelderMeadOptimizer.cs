#region USINGS
using System;
#endregion

namespace Agent.Transformers.Runtime
{
    internal class NelderMeadOptimizer
    {
        private readonly int _maxIterations;
        private readonly double _convergenceThreshold;
        private readonly Random _rng = new(42);

        private const double Alpha = 1.0;
        private const double Gamma = 2.0;
        private const double Rho = 0.5;
        private const double Sigma = 0.5;

        public NelderMeadOptimizer(int maxIterations, double convergenceThreshold)
        {
            _maxIterations = maxIterations;
            _convergenceThreshold = convergenceThreshold;
        }

        public void Optimize(
            double[] initialParams,
            Func<double[], double> objective,
            Action<int, double>? callback = null)
        {
            int n = initialParams.Length;

            var simplex = new double[n + 1][];
            var values = new double[n + 1];

            simplex[0] = (double[])initialParams.Clone();
            values[0] = objective(simplex[0]);

            for (int i = 1; i <= n; i++)
            {
                simplex[i] = (double[])initialParams.Clone();
                simplex[i][i - 1] *= 1.05;
                if (simplex[i][i - 1] == initialParams[i - 1])
                    simplex[i][i - 1] += 0.01;
                values[i] = objective(simplex[i]);
            }

            for (int iter = 0; iter < _maxIterations; iter++)
            {
                Array.Sort(values, simplex);

                double range = Math.Abs(values[n] - values[0]);
                if (range < _convergenceThreshold)
                    break;

                var centroid = new double[n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        centroid[j] += simplex[i][j];
                for (int j = 0; j < n; j++)
                    centroid[j] /= n;

                var reflected = Reflect(centroid, simplex[n]);
                double reflectedValue = objective(reflected);

                if (reflectedValue < values[0])
                {
                    var expanded = Expand(centroid, reflected);
                    double expandedValue = objective(expanded);
                    simplex[n] = expandedValue < reflectedValue ? expanded : reflected;
                    values[n] = Math.Min(expandedValue, reflectedValue);
                }
                else if (reflectedValue < values[n - 1])
                {
                    simplex[n] = reflected;
                    values[n] = reflectedValue;
                }
                else
                {
                    var contracted = Contract(centroid, simplex[n]);
                    double contractedValue = objective(contracted);

                    if (contractedValue < values[n])
                    {
                        simplex[n] = contracted;
                        values[n] = contractedValue;
                    }
                    else
                    {
                        for (int i = 1; i <= n; i++)
                        {
                            for (int j = 0; j < n; j++)
                                simplex[i][j] = simplex[0][j] + Sigma * (simplex[i][j] - simplex[0][j]);
                            values[i] = objective(simplex[i]);
                        }
                    }
                }

                callback?.Invoke(iter, values[0]);
            }

            Array.Copy(simplex[0], initialParams, n);
        }

        private double[] Reflect(double[] centroid, double[] worst)
        {
            int n = centroid.Length;
            var result = new double[n];
            for (int i = 0; i < n; i++)
                result[i] = centroid[i] + Alpha * (centroid[i] - worst[i]);
            return result;
        }

        private double[] Expand(double[] centroid, double[] reflected)
        {
            int n = centroid.Length;
            var result = new double[n];
            for (int i = 0; i < n; i++)
                result[i] = centroid[i] + Gamma * (reflected[i] - centroid[i]);
            return result;
        }

        private double[] Contract(double[] centroid, double[] worst)
        {
            int n = centroid.Length;
            var result = new double[n];
            for (int i = 0; i < n; i++)
                result[i] = centroid[i] + Rho * (worst[i] - centroid[i]);
            return result;
        }
    }
}
