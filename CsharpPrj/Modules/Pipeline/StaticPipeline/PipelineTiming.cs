
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Modules.Pipeline.StaticPipeline
{

    public class StepTiming
    {
        public string StepName { get; }

        public TimeSpan Elapsed { get; }

        public bool IsSuccess { get; }

        public bool IsSkipped { get; }

        public StepTiming(string stepName, TimeSpan elapsed, bool isSuccess, bool isSkipped = false)
        {
            StepName = stepName ?? throw new ArgumentNullException(nameof(stepName));
            Elapsed = elapsed;
            IsSuccess = isSuccess;
            IsSkipped = isSkipped;
        }

        public override string ToString()
            => IsSkipped
                ? $"[SKIP] {StepName} ({Elapsed.TotalMilliseconds:F1}ms)"
                : IsSuccess
                    ? $"[OK]   {StepName} ({Elapsed.TotalMilliseconds:F1}ms)"
                    : $"[FAIL] {StepName} ({Elapsed.TotalMilliseconds:F1}ms)";
    }


    internal class PipelineStopwatch
    {
        private readonly List<StepTiming> _timings = new();
        private readonly Stopwatch _sw = new();
        private TimeSpan _lastTimestamp;

        public IReadOnlyList<StepTiming> Timings => _timings.AsReadOnly();

        public TimeSpan TotalElapsed => _sw.Elapsed;

        public void Start()
        {
            _sw.Start();
            _lastTimestamp = TimeSpan.Zero;
        }

        public StepTiming Record(string stepName, bool isSuccess, bool isSkipped = false)
        {
            var current = _sw.Elapsed;
            var stepElapsed = current - _lastTimestamp;
            _lastTimestamp = current;
            var timing = new StepTiming(stepName, stepElapsed, isSuccess, isSkipped);
            _timings.Add(timing);
            return timing;
        }
    }
}


