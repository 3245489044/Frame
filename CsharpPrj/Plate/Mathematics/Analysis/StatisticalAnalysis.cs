using System;
using System.Collections.Generic;
using System.Linq;

namespace MathematicalAlgorithms
{
    public static class StatisticalAnalysis
    {
        public static double CalculateEntropy(IEnumerable<double> probabilities)
        {
            var probs = probabilities?.ToList() ?? new List<double>();
            if (probs.Count == 0) return 0;

            double entropy = 0;
            foreach (var p in probs)
            {
                if (p > MathConstants.EPSILON)
                    entropy -= p * Math.Log(p, MathConstants.LOG_BASE_E);
            }
            return entropy;
        }

        public static double CalculateEntropy(IEnumerable<int> counts)
        {
            var countList = counts?.ToList() ?? new List<int>();
            if (countList.Count == 0 || countList.Sum() == 0) return 0;

            double total = countList.Sum();
            var probabilities = countList.Select(c => c / total);
            return CalculateEntropy(probabilities);
        }

        public static double CalculateGini(IEnumerable<double> probabilities)
        {
            var probs = probabilities?.ToList() ?? new List<double>();
            if (probs.Count == 0) return 0;

            double sumOfSquares = probs.Sum(p => p * p);
            return 1.0 - sumOfSquares;
        }

        public static double CalculateGini(IEnumerable<int> counts)
        {
            var countList = counts?.ToList() ?? new List<int>();
            if (countList.Count == 0 || countList.Sum() == 0) return 0;

            double total = countList.Sum();
            var probabilities = countList.Select(c => c / total);
            return CalculateGini(probabilities);
        }

        public static double Median(IEnumerable<double> values)
        {
            var list = values?.ToList() ?? new List<double>();
            if (list.Count == 0) return 0;

            list.Sort();
            int n = list.Count;
            return n % 2 == 1 ? list[n / 2] : (list[n / 2 - 1] + list[n / 2]) / 2.0;
        }

        public static double CalculateVariance(IEnumerable<double> values)
        {
            var list = values?.ToList() ?? new List<double>();
            if (list.Count < 2) return 0;

            double mean = list.Average();
            return list.Sum(v => Math.Pow(v - mean, 2)) / (list.Count - 1);
        }

        public static double CalculateSkewness(IEnumerable<double> values)
        {
            var list = values?.ToList() ?? new List<double>();
            if (list.Count < 3) return 0;

            double mean = list.Average();
            double variance = CalculateVariance(list);
            if (variance < MathConstants.EPSILON) return 0;

            double stdDev = Math.Sqrt(variance);
            double skewness = list.Sum(v => Math.Pow((v - mean) / stdDev, 3));
            return skewness * list.Count / ((list.Count - 1) * (list.Count - 2));
        }

        public static double CalculateKurtosis(IEnumerable<double> values)
        {
            var list = values?.ToList() ?? new List<double>();
            if (list.Count < 4) return 0;

            double mean = list.Average();
            double variance = CalculateVariance(list);
            if (variance < MathConstants.EPSILON) return 0;

            double stdDev = Math.Sqrt(variance);
            double kurtosis = list.Sum(v => Math.Pow((v - mean) / stdDev, 4));
            kurtosis = kurtosis * list.Count * (list.Count + 1) / ((list.Count - 1) * (list.Count - 2) * (list.Count - 3));
            double correction = 3.0 * (list.Count - 1) * (list.Count - 1) / ((list.Count - 2) * (list.Count - 3));

            return kurtosis - correction;
        }

        public static double CalculateSilhouetteScore(List<double[]> data, int[] labels, DistanceMetricType metric = DistanceMetricType.Euclidean)
        {
            if (data == null || labels == null || data.Count != labels.Length || data.Count < 2)
                return 0;

            var distanceCalc = DistanceCalculatorFactory.Create(metric);
            int n = data.Count;
            double totalScore = 0;

            for (int i = 0; i < n; i++)
            {
                double a = 0, b = double.MaxValue;
                int aCount = 0;

                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double dist = distanceCalc.Calculate(data[i], data[j]);

                    if (labels[j] == labels[i])
                    {
                        a += dist;
                        aCount++;
                    }
                    else
                    {
                        double clusterDist = 0;
                        int clusterCount = 0;
                        for (int k = 0; k < n; k++)
                        {
                            if (k == i) continue;
                            if (labels[k] == labels[j])
                            {
                                clusterDist += distanceCalc.Calculate(data[i], data[k]);
                                clusterCount++;
                            }
                        }
                        b = Math.Min(b, clusterCount > 0 ? clusterDist / clusterCount : 0);
                    }
                }

                double aMean = aCount > 0 ? a / aCount : 0;
                double maxDist = Math.Max(aMean, b);
                totalScore += maxDist > MathConstants.EPSILON ? (b - aMean) / maxDist : 0;
            }

            return n > 0 ? totalScore / n : 0;
        }
    }
}
