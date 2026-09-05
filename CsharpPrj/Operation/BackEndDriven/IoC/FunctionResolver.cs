
using Microsoft.Extensions.Logging;
using Modules.EventFramework.Abstractions;
using Share.EventBindingOrchestrator.FunctionHub;
using TConfig;

namespace BackEndDriven.IoC;

public class FunctionResolver : IFunctionResolver
{
    private readonly IFunctionHub _functionHub;
    private readonly ILogger<FunctionResolver>? _logger;

    public FunctionResolver(
        IFunctionHub functionHub,
        ILogger<FunctionResolver>? logger = null)
    {
        _functionHub = functionHub ?? throw new ArgumentNullException(nameof(functionHub));
        _logger = logger;
    }

    public WorkflowDefinitionEntry? ResolveWorkflow(string functionName)
    {
        var plan = _functionHub.Resolve(functionName);
        var workflow = plan?.WorkflowDef;
        _logger?.LogDebug("ResolveWorkflow: {FunctionName}, found={Found}, nodeCount={NodeCount}",
            functionName, workflow != null, workflow?.Nodes?.Count ?? 0);
        return workflow;
    }
}
