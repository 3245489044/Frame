
using TConfig;
using ParameterResolver.Converter;

namespace WorkFlowMethod.Usage
{
    public class TypeConverterAdapter : ITypeConverter
    {
        public (object? converted, string? error) Convert(object? value, Type targetType)
        {
            return ParameterResolver.Converter.TypeConverter.Convert(value, targetType);
        }
    }
}
