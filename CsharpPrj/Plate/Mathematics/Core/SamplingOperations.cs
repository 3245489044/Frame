#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public static class SamplingOperations
    {
        #region Softmax（带温度参数）

        public static double[] Softmax(double[] logits, double temperature)
        {
            if (logits == null)
                throw new ArgumentNullException(nameof(logits));
            if (logits.Length == 0)
                return Array.Empty<double>();

            int n = logits.Length;

            if (temperature <= 0)
            {
                int maxIdx = 0;
                for (int i = 1; i < n; i++)
                    if (logits[i] > logits[maxIdx]) maxIdx = i;

                var result = new double[n];
                result[maxIdx] = 1.0;
                return result;
            }

            double maxVal = logits[0];
            for (int i = 1; i < n; i++)
                if (logits[i] > maxVal) maxVal = logits[i];

            var exp = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                exp[i] = Math.Exp((logits[i] - maxVal) / temperature);
                sum += exp[i];
            }

            if (sum > 0)
            {
                for (int i = 0; i < n; i++)
                    exp[i] /= sum;
            }

            return exp;
        }

        #endregion

        #region Top-K 选择

        public static (int index, double prob)[] GetTopK(double[] probs, int k)
        {
            if (probs == null)
                throw new ArgumentNullException(nameof(probs));
            if (k <= 0)
                throw new ArgumentException("k must be positive", nameof(k));

            int n = probs.Length;
            var indexed = new (double prob, int idx)[n];
            for (int i = 0; i < n; i++)
                indexed[i] = (probs[i], i);

            Array.Sort(indexed, (a, b) => b.prob.CompareTo(a.prob));

            int count = Math.Min(k, n);
            var result = new (int index, double prob)[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = (indexed[i].idx, indexed[i].prob);
            }

            return result;
        }

        #endregion

        #region 采样策略

        public static int SampleToken(
            double[] logits,
            double temperature,
            int topK,
            double topP,
            double minP,
            Random rng)
        {
            if (logits == null)
                throw new ArgumentNullException(nameof(logits));
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            int vocabSize = logits.Length;

            double[] scaledLogits;
            if (temperature > 0 && Math.Abs(temperature - 1.0) > 1e-10)
            {
                scaledLogits = new double[vocabSize];
                for (int i = 0; i < vocabSize; i++)
                    scaledLogits[i] = logits[i] / temperature;
            }
            else
            {
                scaledLogits = logits;
            }

            if (temperature <= 0)
            {
                int maxIdx = 0;
                double maxVal = scaledLogits[0];
                for (int i = 1; i < vocabSize; i++)
                {
                    if (scaledLogits[i] > maxVal)
                    {
                        maxVal = scaledLogits[i];
                        maxIdx = i;
                    }
                }
                return maxIdx;
            }

            var probs = Softmax(scaledLogits, 1.0);

            if (minP > 0)
            {
                double maxProb = 0;
                for (int i = 0; i < vocabSize; i++)
                    if (probs[i] > maxProb) maxProb = probs[i];

                double threshold = minP * maxProb;
                for (int i = 0; i < vocabSize; i++)
                    if (probs[i] < threshold) probs[i] = 0;
            }

            if (topK > 0)
            {
                var topKResult = GetTopK(probs, topK);
                for (int i = 0; i < vocabSize; i++)
                    probs[i] = 0;
                foreach (var (idx, prob) in topKResult)
                    probs[idx] = prob;
            }

            if (topP > 0)
            {
                var indexed = new (double prob, int idx)[vocabSize];
                for (int i = 0; i < vocabSize; i++)
                    indexed[i] = (probs[i], i);

                Array.Sort(indexed, (a, b) => b.prob.CompareTo(a.prob));

                double cumulative = 0;
                for (int i = 0; i < vocabSize; i++)
                {
                    if (cumulative >= topP)
                        probs[indexed[i].idx] = 0;
                    cumulative += indexed[i].prob;
                }
            }

            double sum = 0;
            for (int i = 0; i < vocabSize; i++)
                sum += probs[i];

            if (sum <= 0)
            {
                return rng.Next(vocabSize);
            }

            double sample = rng.NextDouble() * sum;
            double cumulative2 = 0;
            for (int i = 0; i < vocabSize; i++)
            {
                cumulative2 += probs[i];
                if (sample < cumulative2)
                    return i;
            }

            return vocabSize - 1;
        }

        public static int SampleFromDistribution(double[] probs, Random rng)
        {
            if (probs == null)
                throw new ArgumentNullException(nameof(probs));
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            double sample = rng.NextDouble();
            double cumulative = 0;

            for (int i = 0; i < probs.Length; i++)
            {
                cumulative += probs[i];
                if (sample < cumulative)
                    return i;
            }

            return probs.Length - 1;
        }

        #endregion

        #region 分布修正

        public static double[] CorrectDistribution(double[] probs, int rejectedToken)
        {
            if (probs == null)
                throw new ArgumentNullException(nameof(probs));

            int n = probs.Length;
            var corrected = new double[n];

            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                if (i != rejectedToken)
                {
                    corrected[i] = Math.Max(0, probs[i] - probs[rejectedToken]);
                    sum += corrected[i];
                }
            }

            if (sum > 0)
            {
                for (int i = 0; i < n; i++)
                    corrected[i] /= sum;
            }

            return corrected;
        }

        #endregion
    }
}
