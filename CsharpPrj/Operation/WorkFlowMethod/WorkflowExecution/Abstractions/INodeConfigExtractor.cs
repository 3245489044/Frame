namespace WorkflowExecution.Abstractions
{
    public interface INodeConfigExtractor<TWorkflow>
    {
        IEnumerable<string> GetNodeIds(TWorkflow workflow);
    }
}
