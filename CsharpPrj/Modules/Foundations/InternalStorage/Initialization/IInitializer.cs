
namespace Modules.InternalStorage.Initialization;

public interface IInitializer
{
    string Name { get; }

    string[] DependsOn { get; }

    Task ExecuteAsync(IInitializationContext context);
}
