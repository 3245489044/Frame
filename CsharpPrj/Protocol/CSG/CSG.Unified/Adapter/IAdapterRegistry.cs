namespace CSG.Unified;

public interface IAdapterRegistry
{
    void Register(AdapterCapabilities capabilities);

    AdapterCapabilities? GetCapabilities(string operationName);

    IReadOnlyList<AdapterCapabilities> GetAllCapabilities();
}
