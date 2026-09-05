#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class IterationSkeleton
    {
        public static async Task<TResult> IterateAsync<TState, TResult>(
            TState initialState,
            int maxIterations,
            Func<TState, int, TState> iterationFunc,
            Func<TState, bool> convergenceCheck,
            Func<TState, TResult> resultSelector,
            IProgress<ComputationProgress> progress = null,
            string algorithmName = "Iteration",
            CancellationToken cancellationToken = default)
        {
            var state = initialState;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                state = iterationFunc(state, iter);

                progress?.Report(new ComputationProgress(
                    (double)(iter + 1) / maxIterations * 100,
                    $"{algorithmName} iteration {iter + 1}/{maxIterations}"));

                if (convergenceCheck(state))
                {
                    progress?.Report(new ComputationProgress(100,
                        $"{algorithmName} converged at iteration {iter + 1}"));
                    break;
                }
            }

            return resultSelector(state);
        }

        public static TResult Iterate<TState, TResult>(
            TState initialState,
            int maxIterations,
            Func<TState, int, TState> iterationFunc,
            Func<TState, bool> convergenceCheck,
            Func<TState, TResult> resultSelector,
            IProgress<ComputationProgress> progress = null,
            string algorithmName = "Iteration")
        {
            var state = initialState;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                state = iterationFunc(state, iter);

                progress?.Report(new ComputationProgress(
                    (double)(iter + 1) / maxIterations * 100,
                    $"{algorithmName} iteration {iter + 1}/{maxIterations}"));

                if (convergenceCheck(state))
                {
                    progress?.Report(new ComputationProgress(100,
                        $"{algorithmName} converged at iteration {iter + 1}"));
                    break;
                }
            }

            return resultSelector(state);
        }

        public static TResult DampedIterate<TState, TResult>(
            TState initialState,
            int maxIterations,
            Func<TState, int, TState> iterationFunc,
            Func<TState, TState, double> changeFunc,
            double convergenceThreshold,
            Func<TState, TResult> resultSelector,
            IProgress<ComputationProgress> progress = null,
            string algorithmName = "DampedIteration")
        {
            var state = initialState;
            var previousState = initialState;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                previousState = state;
                state = iterationFunc(state, iter);

                double change = changeFunc(state, previousState);

                progress?.Report(new ComputationProgress(
                    (double)(iter + 1) / maxIterations * 100,
                    $"{algorithmName} iteration {iter + 1}/{maxIterations}, change={change:F6}"));

                if (change < convergenceThreshold)
                {
                    progress?.Report(new ComputationProgress(100,
                        $"{algorithmName} converged at iteration {iter + 1}"));
                    break;
                }
            }

            return resultSelector(state);
        }
    }
}
