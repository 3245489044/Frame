
using TConfig;

namespace Modules.EventFramework.Abstractions;

public interface IFunctionResolver
{
    WorkflowDefinitionEntry? ResolveWorkflow(string functionName);
}
