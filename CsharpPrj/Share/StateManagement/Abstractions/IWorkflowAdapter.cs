
namespace Share.StateManagement
{
    public interface IWorkflowAdapter
    {
        string Name { get; }

        void Adapt(IStageContext context);
    }
}
