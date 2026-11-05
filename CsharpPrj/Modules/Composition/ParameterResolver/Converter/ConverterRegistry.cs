
using System.Collections.Concurrent;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Converter;

public class ConverterRegistry : IConverterRegistry
{
    private readonly ConcurrentDictionary<Type, IValueConverter> _converters = new();

    public void Register(Type targetType, IValueConverter converter)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(converter);
        _converters[targetType] = converter;
    }

    public void Register<TTarget>(IValueConverter converter)
    {
        Register(typeof(TTarget), converter);
    }

    public IValueConverter? GetConverter(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        return _converters.TryGetValue(targetType, out var converter) ? converter : null;
    }

    public bool HasConverter(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        return _converters.ContainsKey(targetType);
    }

    public void RegisterProvider(IConverterProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        foreach (var (type, converter) in provider.GetConverters())
        {
            _converters.TryAdd(type, converter);
        }
    }

    public IEnumerable<KeyValuePair<Type, IValueConverter>> GetAllConverters()
    {
        return _converters;
    }
}
