#region USINGS
using System;
using System.Collections.Generic;
#endregion

namespace MathematicalAlgorithms
{
    public record MatrixShape(int Rows, int Columns)
    {
        public long TotalElements => (long)Rows * Columns;
        public long EstimatedMemoryBytes => TotalElements * sizeof(double);

        public override string ToString() => $"[{Rows}×{Columns}]";
    }

    public record MatrixComputationConfig
    {
        public bool EnableParallel { get; init; } = true;
        public bool EnableCaching { get; init; } = true;
        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
        public bool EnableProgressReporting { get; init; } = true;
        public int ChunkSize { get; init; } = 1000;
        public double MemoryLimitPercentage { get; init; } = 0.8;
        public int ParallelThreshold { get; init; } = 100;
        public CachePolicy CachePolicy { get; init; } = CachePolicy.Default;
        public long? MemoryLimitBytes { get; init; } = null;

        public static MatrixComputationConfig Default => new();
    }

    public record CachePolicy
    {
        public TimeSpan AbsoluteExpiration { get; init; } = TimeSpan.FromHours(1);
        public static CachePolicy Default => new();
    }

    public record AlgorithmMetadata(
        string AlgorithmName,
        string AlgorithmVersion = "1.0.0",
        DateTime Timestamp = default,
        IReadOnlyDictionary<string, object> Parameters = null,
        MatrixShape InputShape = null,
        MatrixShape OutputShape = null,
        double MemoryUsageMB = 0,
        double ComputationTimeMs = 0)
    {
        public AlgorithmMetadata() : this(string.Empty) { }
    }

    public record MatrixResult(
        MathNet.Numerics.LinearAlgebra.Matrix<double> Matrix,
        TimeSpan ComputationTime,
        AlgorithmMetadata Metadata,
        bool IsCached = false)
    {
        public double ComputationTimeMs => ComputationTime.TotalMilliseconds;
    }
}
