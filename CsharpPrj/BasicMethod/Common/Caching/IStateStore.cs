
namespace Modules.Common.Caching
{
    public interface IStateStore
    {
        void Set(string key, string? value);

        string? Get(string key);

        bool Has(string key);

        void Clear();

        IReadOnlyCollection<string> GetAllKeys();
    }
}
