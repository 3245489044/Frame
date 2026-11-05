
namespace Modules.EventFramework.Abstractions;

public interface IControlAdapterRegistry
{
    object? GetOutputAdapter(object control);

    object? GetInputAdapter(object control);
}
