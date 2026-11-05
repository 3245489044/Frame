
namespace Modules.DiagnosticContext;

public class BatchContextData
{
    public string BatchId { get; set; } = string.Empty;

    public string BatchName { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? WorkflowId { get; set; }

    public string? ProjectRoot { get; set; }

    public bool IsPipeMode { get; set; }

    public string? PipeName { get; set; }
}
