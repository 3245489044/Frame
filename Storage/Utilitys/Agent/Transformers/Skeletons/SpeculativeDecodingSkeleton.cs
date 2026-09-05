#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class SpeculativeDecodingSkeleton
    {
        public static int[,] Decode(
            int[,] inputTokens,
            int maxNewTokens,
            Func<int[,], int[,]> draftModel,
            Func<int[,], double[,]> targetModel,
            int numSpeculations = 5,
            double temperature = 1.0,
            Random? rng = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (inputTokens == null) throw new ArgumentNullException(nameof(inputTokens));
            if (draftModel == null) throw new ArgumentNullException(nameof(draftModel));
            if (targetModel == null) throw new ArgumentNullException(nameof(targetModel));
            if (maxNewTokens <= 0)
                throw new ArgumentException("maxNewTokens must be positive", nameof(maxNewTokens));
            if (numSpeculations <= 0)
                throw new ArgumentException("numSpeculations must be positive", nameof(numSpeculations));

            rng ??= new Random();
            int batchSize = inputTokens.GetLength(0);
            int inputLen = inputTokens.GetLength(1);

            var result = new int[batchSize, maxNewTokens];
            int generatedCount = 0;

            while (generatedCount < maxNewTokens)
            {
                int speculateCount = Math.Min(numSpeculations, maxNewTokens - generatedCount);
                var draftTokens = GenerateDraft(inputTokens, speculateCount, draftModel, temperature, rng);

                var accepted = VerifyTokens(
                    inputTokens, draftTokens, targetModel, temperature, rng, out int numAccepted);

                for (int b = 0; b < batchSize; b++)
                {
                    for (int i = 0; i < numAccepted && generatedCount + i < maxNewTokens; i++)
                    {
                        result[b, generatedCount + i] = accepted[b, i];
                    }
                }

                generatedCount += numAccepted;

                int newLen = inputLen + numAccepted;
                var newInput = new int[batchSize, newLen];
                for (int b = 0; b < batchSize; b++)
                {
                    for (int i = 0; i < inputLen; i++)
                        newInput[b, i] = inputTokens[b, i];
                    for (int i = 0; i < numAccepted; i++)
                        newInput[b, inputLen + i] = accepted[b, i];
                }
                inputTokens = newInput;
                inputLen = newLen;

                progress?.Report(new ComputationProgress(
                    (int)((double)generatedCount / maxNewTokens * 100),
                    $"Speculative Decoding: {generatedCount}/{maxNewTokens}"));
            }

            return result;
        }

        private static int[,] GenerateDraft(
            int[,] inputTokens,
            int numTokens,
            Func<int[,], int[,]> draftModel,
            double temperature,
            Random rng)
        {
            int batchSize = inputTokens.GetLength(0);
            int inputLen = inputTokens.GetLength(1);

            var currentInput = inputTokens;
            var draftTokens = new int[batchSize, numTokens];

            for (int i = 0; i < numTokens; i++)
            {
                var nextTokens = draftModel(currentInput);

                int seqLen = nextTokens.GetLength(1);
                for (int b = 0; b < batchSize; b++)
                {
                    draftTokens[b, i] = nextTokens[b, seqLen - 1];
                }

                int newLen = currentInput.GetLength(1) + 1;
                var newInput = new int[batchSize, newLen];
                for (int b = 0; b < batchSize; b++)
                {
                    for (int j = 0; j < currentInput.GetLength(1); j++)
                        newInput[b, j] = currentInput[b, j];
                    newInput[b, currentInput.GetLength(1)] = draftTokens[b, i];
                }
                currentInput = newInput;
            }

            return draftTokens;
        }

        private static int[,] VerifyTokens(
            int[,] inputTokens,
            int[,] draftTokens,
            Func<int[,], double[,]> targetModel,
            double temperature,
            Random rng,
            out int numAccepted)
        {
            int batchSize = inputTokens.GetLength(0);
            int inputLen = inputTokens.GetLength(1);
            int numDraft = draftTokens.GetLength(1);

            int totalLen = inputLen + numDraft;
            var combinedInput = new int[batchSize, totalLen];
            for (int b = 0; b < batchSize; b++)
            {
                for (int i = 0; i < inputLen; i++)
                    combinedInput[b, i] = inputTokens[b, i];
                for (int i = 0; i < numDraft; i++)
                    combinedInput[b, inputLen + i] = draftTokens[b, i];
            }

            var logits = targetModel(combinedInput);
            int seqLen = logits.GetLength(1);
            int vocabSize = logits.GetLength(2);

            var accepted = new int[batchSize, numDraft];
            numAccepted = 0;

            for (int i = 0; i < numDraft; i++)
            {
                bool allAccepted = true;

                for (int b = 0; b < batchSize; b++)
                {
                    var targetProbs = SamplingOperations.Softmax(GetRow3D(logits, b, inputLen + i), temperature);

                    int draftToken = draftTokens[b, i];

                    double targetProb = targetProbs[draftToken];
                    double sample = rng.NextDouble();

                    if (sample > targetProb)
                    {
                        var correctedProbs = SamplingOperations.CorrectDistribution(targetProbs, draftToken);
                        accepted[b, i] = SamplingOperations.SampleFromDistribution(correctedProbs, rng);
                        allAccepted = false;
                    }
                    else
                    {
                        accepted[b, i] = draftToken;
                    }
                }

                numAccepted++;

                if (!allAccepted)
                    break;
            }

            return accepted;
        }

        private static double[] GetRow3D(double[,] array, int batch, int seq)
        {
            int cols = array.GetLength(1);
            var result = new double[cols];
            for (int i = 0; i < cols; i++)
                result[i] = array[batch * cols + seq, i];
            return result;
        }

        public static Task<int[,]> DecodeAsync(
            int[,] inputTokens,
            int maxNewTokens,
            Func<int[,], int[,]> draftModel,
            Func<int[,], double[,]> targetModel,
            int numSpeculations = 5,
            double temperature = 1.0,
            Random? rng = null,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Decode(inputTokens, maxNewTokens, draftModel, targetModel,
                    numSpeculations, temperature, rng, progress);
            }, cancellationToken);
        }
    }
}
