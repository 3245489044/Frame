
namespace ParameterResolver.Strategies.Abstractions;

public interface IReadStrategy<TAdapter>
    where TAdapter : class
{
    object? Read(object source, TAdapter? adapter);
}
