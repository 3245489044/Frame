
namespace Modules.EventFramework.Services;

public interface IWriteStrategy
{
    void Write(object target, object? value, object? adapter);
}
