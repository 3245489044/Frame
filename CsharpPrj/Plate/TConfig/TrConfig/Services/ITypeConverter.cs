
namespace TConfig
{
    public interface ITypeConverter
    {
        (object? converted, string? error) Convert(object? value, Type targetType);
    }
}
