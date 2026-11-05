
namespace ParameterResolver.Strategies.Abstractions;

public interface IWriteStrategy<TAdapter>
    where TAdapter : class
{
    void Write(object target, object? value, TAdapter? adapter);
}
