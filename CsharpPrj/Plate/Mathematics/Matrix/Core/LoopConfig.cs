#region USINGS
using System;
using System.Threading;
#endregion

namespace MathematicalAlgorithms
{
    public record LoopShape(int OuterCount, int InnerCount)
    {
        public long TotalElements => (long)OuterCount * InnerCount;
        public override string ToString() => $"[{OuterCount}×{InnerCount}]";
    }

    public record LoopConfig
    {
        public bool EnableParallel { get; init; } = true;
        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
        public bool EnableProgressReporting { get; init; } = true;
        public int ParallelThreshold { get; init; } = 100;
        public int ProgressReportInterval { get; init; } = 100;

        public static LoopConfig Default => new();
        public static LoopConfig Sequential => new() { EnableParallel = false };
    }

}
