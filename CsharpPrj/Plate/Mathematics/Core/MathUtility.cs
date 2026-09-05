using System;
using System.Collections.Generic;
using System.Linq;

namespace MathematicalAlgorithms
{
    public interface IMathUtility
    {
        double SafeLog(double value, double baseValue = Math.E);
        double WeightedAverage(IEnumerable<double> values, IEnumerable<double> weights);
        double StandardDeviation(IEnumerable<double> values, bool isSample = true);
        double Percentile(IEnumerable<double> values, double percentile);
        double LinearInterpolation(double x, double x0, double x1, double y0, double y1);
    }

    public class MathUtility : IMathUtility
    {
        public double SafeLog(double value, double baseValue = Math.E)
        {
            if (value <= 0 || baseValue <= 0 || Math.Abs(baseValue - 1.0) < MathConstants.EPSILON)
                return 0;

            return Math.Log(Math.Max(MathConstants.EPSILON, value), Math.Max(MathConstants.EPSILON, baseValue));
        }

        public double WeightedAverage(IEnumerable<double> values, IEnumerable<double> weights)
        {
            var valuesList = values?.ToList() ?? new List<double>();
            var weightsList = weights?.ToList() ?? new List<double>();

            if (valuesList.Count == 0 || weightsList.Count == 0 || valuesList.Count != weightsList.Count)
                return 0;

            double weightedSum = 0;
            double totalWeight = 0;

            for (int i = 0; i < valuesList.Count; i++)
            {
                weightedSum += valuesList[i] * weightsList[i];
                totalWeight += weightsList[i];
            }

            return Math.Abs(totalWeight) < MathConstants.EPSILON ? 0 : weightedSum / totalWeight;
        }

        public double StandardDeviation(IEnumerable<double> values, bool isSample = true)
        {
            var valueList = values?.ToList() ?? new List<double>();

            if (valueList.Count < MathConstants.MINIMUM_SAMPLE_SIZE)
                return 0;

            var mean = valueList.Average();
            var sumOfSquares = valueList.Sum(x => Math.Pow(x - mean, 2));
            var divisor = isSample ? Math.Max(1, valueList.Count - 1) : valueList.Count;

            return divisor > 0 ? Math.Sqrt(sumOfSquares / divisor) : 0;
        }

        public double Percentile(IEnumerable<double> values, double percentile)
        {
            var sorted = values?.OrderBy(x => x).ToList() ?? new List<double>();

            if (sorted.Count == 0)
                return 0;

            double normalizedPercentile = Math.Clamp(percentile, 0, 100);
            double index = (sorted.Count - 1) * normalizedPercentile / 100.0;
            int lowerIndex = (int)Math.Floor(index);

            if (lowerIndex < 0) lowerIndex = 0;
            if (lowerIndex >= sorted.Count - 1)
                return sorted[^1];

            double weight = index - lowerIndex;
            return sorted[lowerIndex] * (1 - weight) + sorted[lowerIndex + 1] * weight;
        }

        public double LinearInterpolation(double x, double x0, double x1, double y0, double y1)
        {
            if (Math.Abs(x1 - x0) < MathConstants.EPSILON)
                return y0;

            return y0 + (x - x0) * (y1 - y0) / (x1 - x0);
        }
    }
}
