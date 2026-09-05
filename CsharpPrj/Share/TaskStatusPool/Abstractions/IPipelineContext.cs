
#nullable enable

namespace Share.TaskStatusPool.Abstractions
{
    public interface IPipelineContext
    {

        int TotalCommands { get; set; }

        int SuccessCount { get; set; }

        int FailCount { get; set; }

        int SkipCount { get; set; }

        int CurrentIndex { get; set; }


        string? Mode { get; set; }

        string? BatchId { get; set; }

        string? ConfigPath { get; set; }

        string? CommandFile { get; set; }

        bool AutoClose { get; set; }


        bool IsCancelled { get; set; }

        bool IsExecuting { get; set; }


        Dictionary<string, object?> SharedData { get; }


        void Reset();

        string GetSummary();
    }
}
