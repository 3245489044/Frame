
namespace Modules.EventFramework.Services;

public interface IStrategyResolver
{
    (object Strategy, bool HasExplicitConfig) ResolveWrite(object? adapter);

    (object Strategy, bool HasExplicitConfig) ResolveRead(object? adapter);
}
