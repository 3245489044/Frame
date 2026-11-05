
namespace ParameterResolver.Abstractions;

public interface IConverterRegistry
{
    void Register(Type targetType, IValueConverter converter);

    void Register<TTarget>(IValueConverter converter);

    IValueConverter? GetConverter(Type targetType);

    bool HasConverter(Type targetType);

    void RegisterProvider(IConverterProvider provider);

    IEnumerable<KeyValuePair<Type, IValueConverter>> GetAllConverters();
}

public interface IConverterProvider
{
    string Name { get; }

    IEnumerable<KeyValuePair<Type, IValueConverter>> GetConverters();
}
