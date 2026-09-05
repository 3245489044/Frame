#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class BeamSearchSkeleton
    {
        private static readonly LoopEngine _loopEngine = new();

        public static int[,] Search(
            int[,] initialTokens,
            int maxNewTokens,
            int beamSize,
            int vocabSize,
            Func<int[,], double[,]> getLogits,
            double lengthPenalty = 1.0,
            double repetitionPenalty = 1.0,
            int eosTokenId = -1,
            IProgress<ComputationProgress>? progress = null)
        {
            if (initialTokens == null) throw new ArgumentNullException(nameof(initialTokens));
            if (getLogits == null) throw new ArgumentNullException(nameof(getLogits));
            if (maxNewTokens <= 0)
                throw new ArgumentException("maxNewTokens must be positive", nameof(maxNewTokens));
            if (beamSize <= 0)
                throw new ArgumentException("beamSize must be positive", nameof(beamSize));
            if (vocabSize <= 0)
                throw new ArgumentException("vocabSize must be positive", nameof(vocabSize));

            int batchSize = initialTokens.GetLength(0);
            int initialLen = initialTokens.GetLength(1);

            var result = new int[batchSize, maxNewTokens];

            for (int b = 0; b < batchSize; b++)
            {
                var beams = new List<BeamCandidate>
                {
                    new BeamCandidate
                    {
                        Tokens = GetRow(initialTokens, b).ToList(),
                        Score = 0.0
                    }
                };

                int generatedCount = 0;
                bool allFinished = false;

                while (generatedCount < maxNewTokens && !allFinished)
                {
                    var candidates = new List<BeamCandidate>();

                    foreach (var beam in beams)
                    {
                        if (beam.Finished)
                        {
                            candidates.Add(beam);
                            continue;
                        }

                        var input = new int[1, beam.Tokens.Count];
                        for (int i = 0; i < beam.Tokens.Count; i++)
                            input[0, i] = beam.Tokens[i];

                        var logits = getLogits(input);
                        int totalLen = logits.GetLength(0);

                        var lastLogits = new double[vocabSize];
                        for (int v = 0; v < vocabSize; v++)
                            lastLogits[v] = logits[totalLen - 1, v];

                        if (repetitionPenalty > 0 && repetitionPenalty != 1.0)
                        {
                            foreach (int token in beam.Tokens)
                            {
                                if (token >= 0 && token < vocabSize)
                                {
                                    if (lastLogits[token] < 0)
                                        lastLogits[token] *= repetitionPenalty;
                                    else
                                        lastLogits[token] /= repetitionPenalty;
                                }
                            }
                        }

                        var probs = SoftmaxCalculator.Compute(lastLogits);

                        var topTokens = SamplingOperations.GetTopK(probs, beamSize);

                        foreach (var (token, prob) in topTokens)
                        {
                            var newTokens = new List<int>(beam.Tokens) { token };
                            double newScore = beam.Score + Math.Log(prob);

                            double normalizedScore = newScore / Math.Pow(newTokens.Count, lengthPenalty);

                            candidates.Add(new BeamCandidate
                            {
                                Tokens = newTokens,
                                Score = newScore,
                                NormalizedScore = normalizedScore,
                                Finished = token == eosTokenId
                            });
                        }
                    }

                    beams = candidates
                        .OrderByDescending(c => c.NormalizedScore)
                        .Take(beamSize)
                        .ToList();

                    generatedCount++;

                    allFinished = beams.All(b => b.Finished);

                    progress?.Report(new ComputationProgress(
                        (int)((double)generatedCount / maxNewTokens * 100),
                        $"Beam Search: Step {generatedCount}/{maxNewTokens}"));
                }

                var bestBeam = beams.OrderByDescending(c => c.NormalizedScore).First();

                for (int i = 0; i < Math.Min(bestBeam.Tokens.Count - initialLen, maxNewTokens); i++)
                {
                    result[b, i] = bestBeam.Tokens[initialLen + i];
                }
            }

            return result;
        }

        private class BeamCandidate
        {
            public List<int> Tokens { get; set; } = new();
            public double Score { get; set; }
            public double NormalizedScore { get; set; }
            public bool Finished { get; set; }
        }

        private static int[] GetRow(int[,] array, int row)
        {
            int cols = array.GetLength(1);
            var result = new int[cols];
            for (int i = 0; i < cols; i++)
                result[i] = array[row, i];
            return result;
        }

        public static Task<int[,]> SearchAsync(
            int[,] initialTokens,
            int maxNewTokens,
            int beamSize,
            int vocabSize,
            Func<int[,], double[,]> getLogits,
            double lengthPenalty = 1.0,
            double repetitionPenalty = 1.0,
            int eosTokenId = -1,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Search(initialTokens, maxNewTokens, beamSize, vocabSize,
                    getLogits, lengthPenalty, repetitionPenalty, eosTokenId, progress);
            }, cancellationToken);
        }
    }
}
