
using TConfig.ContextFrame.Abstractions;
using Modules.Pipeline;
using ParameterResolver.Models;

namespace WorkFlowMethod.Output;

public class OutputWriteContext : PipelineContext
{

    public object Sender { get; init; } = null!;

    public string? OutputTarget { get; init; }

    public object? RawValue { get; set; }


    public object? TargetControl { get; set; }

    public OutputAdapter? Adapter { get; set; }

    public ILayeredSnapshot? Snapshot { get; set; }


    public bool WriteSuccess { get; set; }

    public string? ErrorMessage { get; set; }


    public IStorageCell Storage { get; init; } = null!;

    public OutputWriteContext()
    {
        Mode = "Output";
    }
}
