
#nullable enable

namespace Share.TaskStatusPool.Core
{
    public class PipelineContext : Abstractions.IPipelineContext
    {

        public int TotalCommands { get; set; }

        public int SuccessCount { get; set; }

        public int FailCount { get; set; }

        public int SkipCount { get; set; }

        public int CurrentIndex { get; set; } = -1;


        public string? Mode { get; set; }

        public string? BatchId { get; set; }

        public string? ConfigPath { get; set; }

        public string? CommandFile { get; set; }

        public bool AutoClose { get; set; }


        public bool IsCancelled { get; set; }

        public bool IsExecuting { get; set; }


        public Dictionary<string, object?> SharedData { get; set; } = new(StringComparer.OrdinalIgnoreCase);


        public virtual void Reset()
        {
            TotalCommands = 0;
            SuccessCount = 0;
            FailCount = 0;
            SkipCount = 0;
            IsCancelled = false;
            IsExecuting = false;
            SharedData.Clear();
            CurrentIndex = -1;
        }

        public virtual string GetSummary()
            => $"Total={TotalCommands}, Success={SuccessCount}, Fail={FailCount}, Skip={SkipCount}";
    }
}
