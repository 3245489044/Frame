#region USINGS
using System;
using System.Collections.Generic;
#endregion

namespace MathematicalAlgorithms
{
    public record TransformResult<TOutput>(
        TOutput Output,
        TimeSpan ComputationTime,
        TransformMetadata Metadata,
        bool IsCached = false)
    {
        public double ComputationTimeMs => ComputationTime.TotalMilliseconds;
    }

    public record TransformMetadata(
        string TransformName,
        string TransformVersion = "1.0.0",
        DateTime Timestamp = default,
        IReadOnlyDictionary<string, object> Parameters = null,
        double MemoryUsageMB = 0,
        double ComputationTimeMs = 0)
    {
        public TransformMetadata() : this(string.Empty) { }
    }
}
