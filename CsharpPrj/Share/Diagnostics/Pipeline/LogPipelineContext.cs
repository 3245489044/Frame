
using Modules.DiagnosticContext;
using BP = Modules.Pipeline;

namespace Common.Diagnostics
{
    public class LogPipelineContext : BP.PipelineContext
    {
        public BatchContextData BatchData { get; set; } = new();

        public string BatchId
        {
            get => BatchData.BatchId;
            set => BatchData.BatchId = value;
        }

        public string BatchName
        {
            get => BatchData.BatchName;
            set => BatchData.BatchName = value;
        }

        public string Source
        {
            get => BatchData.Source;
            set => BatchData.Source = value;
        }

        public string? Description
        {
            get => BatchData.Description;
            set => BatchData.Description = value;
        }

        public string? WorkflowId
        {
            get => BatchData.WorkflowId;
            set => BatchData.WorkflowId = value;
        }

        public string? ProjectRoot
        {
            get => BatchData.ProjectRoot;
            set => BatchData.ProjectRoot = value;
        }

        public bool IsPipeMode
        {
            get => BatchData.IsPipeMode;
            set => BatchData.IsPipeMode = value;
        }

        public string? PipeName
        {
            get => BatchData.PipeName;
            set => BatchData.PipeName = value;
        }

        public LogCategory EnabledCategories { get; set; } = LogCategory.None;

        public string LogLevel { get; set; } = "Info";


        public int TotalCommands { get; set; }

        public int SuccessCount { get; set; }

        public int FailCount { get; set; }

        public static LogPipelineContext CreateDefault()
        {
            return new LogPipelineContext
            {
                BatchId = Guid.NewGuid().ToString("N"),
                BatchName = $"Batch_{DateTime.Now:yyyyMMdd_HHmmss}",
                Source = "Default",
                LogLevel = "Info",
                EnabledCategories = LogCategory.None
            };
        }

    }
}
