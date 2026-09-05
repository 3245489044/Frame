
namespace TConfig.Controls;

public interface IControlRegistry
{
    void Register(string name, object control);

    void Unregister(string name);

    object? Find(string name);

    bool TryGetValue(string name, out object? control);

    void Clear();
}
