
namespace Modules.EventFramework.Services;

public interface IReadStrategy
{
    object? Read(object source, object? adapter);
}
