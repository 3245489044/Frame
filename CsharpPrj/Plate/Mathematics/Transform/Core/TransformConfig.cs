#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public record TransformConfig
    {
        public bool EnableParallel { get; init; } = true;

        public bool EnableCaching { get; init; } = true;

        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

        public bool EnableProgressReporting { get; init; } = true;

        public double MemoryLimitPercentage { get; init; } = 0.8;

        public int ParallelThreshold { get; init; } = 100;

        public CachePolicy CachePolicy { get; init; } = CachePolicy.Default;

        public long? MemoryLimitBytes { get; init; } = null;

        public int ChunkSize { get; init; } = 1000;

        public int ProgressReportInterval { get; init; } = 100;

        public long MaxCacheSizeBytes { get; init; } = 100 * 1024 * 1024;

        public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(5);

        public static TransformConfig Default => new();
    }
}
