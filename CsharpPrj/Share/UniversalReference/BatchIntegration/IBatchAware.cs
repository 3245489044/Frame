
namespace UniversalWorkflowFramework.BatchIntegration
{
    public interface IBatchAware
    {
        string BatchId { get; }

        string BatchName { get; }

        string Source { get; }
    }
}
