
namespace Modules.StateManagement
{
    public interface IVirtualStore
    {
        void SetValue(string controlName, string? value);

        string? GetValue(string controlName);

        bool HasControl(string controlName);

        void RemoveControl(string controlName);

        void RemoveByPrefix(string prefix);

        void ClearAll();

        IReadOnlyCollection<string> GetAllControlNames();

        Dictionary<string, string?> GetAllValues();

        void SetRange(IEnumerable<KeyValuePair<string, string?>> values);

        int Count { get; }
    }
}
