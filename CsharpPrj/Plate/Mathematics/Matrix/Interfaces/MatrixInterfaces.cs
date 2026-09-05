#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public interface IMatrixAlgorithm<TInput>
    {
        Task<MatrixResult> ComputeAsync(
            TInput input,
            MatrixComputationConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default);

        MatrixShape GetExpectedShape(TInput input);
        Func<TInput, int, int, double> GetCellFunction();
    }

    public interface IMatrixCalculator : IDisposable
    {
        Task<MatrixResult> ComputeAsync<TInput>(
            string algorithmName,
            TInput input,
            MatrixComputationConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default);

        void RegisterAlgorithm<TInput>(string name, IMatrixAlgorithm<TInput> algorithm);
        bool HasAlgorithm(string name);
        IReadOnlyCollection<string> GetAlgorithmNames();
    }
}
