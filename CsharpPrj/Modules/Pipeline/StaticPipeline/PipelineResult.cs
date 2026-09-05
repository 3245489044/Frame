
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.Pipeline.StaticPipeline
{

    public enum PipelineResultStatus
    {
        Success,

        PartialSuccess,

        Failed
    }


    public class StepError
    {
        public string StepName { get; }

        public Exception Exception { get; }

        public bool IsFatal { get; }

        public StepError(string stepName, Exception exception, bool isFatal = true)
        {
            StepName = stepName ?? throw new ArgumentNullException(nameof(stepName));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            IsFatal = isFatal;
        }

        public override string ToString()
            => $"[{(IsFatal ? "FATAL" : "ERROR")}] Step '{StepName}': {Exception.Message}";
    }


    public class PipelineResult
    {
        public PipelineResultStatus Status { get; }

        public bool IsSuccess => Status == PipelineResultStatus.Success;

        public bool IsPartialSuccess => Status == PipelineResultStatus.PartialSuccess;

        public bool IsFailed => Status == PipelineResultStatus.Failed;

        public IReadOnlyList<StepError> Errors { get; }

        public bool HasErrors => Errors.Count > 0;

        public bool HasFatalErrors { get; }

        public IReadOnlyList<StepTiming>? Timings { get; }

        public TimeSpan? TotalElapsed
        {
            get
            {
                if (Timings == null || Timings.Count == 0)
                    return null;

                long totalTicks = 0;
                foreach (var t in Timings)
                    totalTicks += t.Elapsed.Ticks;
                return TimeSpan.FromTicks(totalTicks);
            }
        }


        private PipelineResult(
            PipelineResultStatus status,
            List<StepError>? errors = null,
            IReadOnlyList<StepTiming>? timings = null)
        {
            Status = status;
            Errors = errors?.AsReadOnly() ?? new List<StepError>().AsReadOnly();
            HasFatalErrors = errors?.Exists(e => e.IsFatal) ?? false;
            Timings = timings;
        }

        public static PipelineResult Success(IReadOnlyList<StepTiming>? timings = null)
            => new(PipelineResultStatus.Success, timings: timings);

        public static PipelineResult PartialSuccess(
            List<StepError> errors,
            IReadOnlyList<StepTiming>? timings = null)
            => new(PipelineResultStatus.PartialSuccess, errors, timings);

        public static PipelineResult Failed(
            List<StepError> errors,
            IReadOnlyList<StepTiming>? timings = null)
            => new(PipelineResultStatus.Failed, errors, timings);

        public StepError? FirstFatalError
            => Errors.FirstOrDefault(e => e.IsFatal);

        public StepError? FirstError
            => Errors.Count > 0 ? Errors[0] : null;

        public StepTiming? GetTiming(string stepName)
            => Timings?.FirstOrDefault(t => t.StepName == stepName);

        public override string ToString()
        {
            var timingInfo = TotalElapsed.HasValue
                ? $" ({TotalElapsed.Value.TotalMilliseconds:F1}ms)"
                : "";

            return Status switch
            {
                PipelineResultStatus.Success => $"Success{timingInfo}",
                PipelineResultStatus.PartialSuccess
                    => $"PartialSuccess{timingInfo} ({Errors.Count} non-fatal errors)",
                PipelineResultStatus.Failed
                    => $"Failed{timingInfo} ({Errors.Count} errors, {Errors.Count(e => e.IsFatal)} fatal)",
                _ => Status.ToString()
            };
        }
    }
}
