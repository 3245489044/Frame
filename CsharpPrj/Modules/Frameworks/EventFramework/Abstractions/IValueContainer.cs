
namespace Modules.EventFramework.Abstractions;

public interface IValueContainer
{
    bool CanRead { get; }

    bool CanWrite { get; }

    object? GetValue();

    void SetValue(object? value);
}
