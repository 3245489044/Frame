
namespace Share.StateManagement
{
    public interface IStateManager
    {
        IStateStore GlobalStore { get; }

        IStateStore CreateScope(string scopeName);

        IStateStore? GetScope(string scopeName);

        bool ReleaseScope(string scopeName);

        IReadOnlyCollection<string> GetScopeNames();
    }
}
