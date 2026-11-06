
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using FunctionStorage.Abstractions;

namespace FunctionStorage.Core
{

    public static class TypeConverter
    {
        public static object?[] ConvertArguments(object?[] arguments, Type[] targetTypes)
        {
            if (arguments == null || targetTypes == null)
                throw new ArgumentNullException(arguments == null ? nameof(arguments) : nameof(targetTypes));

            if (arguments.Length != targetTypes.Length)
                throw new ArgumentException(
                    $"Argument count mismatch: {arguments.Length} arguments vs {targetTypes.Length} target types");

            var result = new object?[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                result[i] = ConvertValue(arguments[i], targetTypes[i]);
            }
            return result;
        }

        public static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            var sourceType = value.GetType();

            if (targetType.IsInstanceOfType(value))
                return value;

            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
                return ConvertValue(value, underlyingType);

            if (value is string strValue)
                return ConvertFromString(strValue, targetType);

            try
            {
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(targetType);
                if (converter.CanConvertFrom(sourceType))
                    return converter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
            }
            catch { }

            try
            {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return value;
            }
        }

        private static object? ConvertFromString(string value, Type targetType)
        {
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, ignoreCase: true);

            try
            {
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(targetType);
                if (converter.CanConvertFrom(typeof(string)))
                    return converter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
            }
            catch { }

            try
            {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return value;
            }
        }

        public static bool CanConvert(Type sourceType, Type targetType)
        {
            if (sourceType.IsAbstract || sourceType.IsInterface || sourceType.IsArray)
            {
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(targetType);
                return converter.CanConvertFrom(sourceType);
            }

            try
            {
                if (targetType.IsInstanceOfType(Activator.CreateInstance(sourceType)))
                    return true;
            }
            catch (MissingMethodException) { }
            catch (MemberAccessException) { }

            var converter2 = System.ComponentModel.TypeDescriptor.GetConverter(targetType);
            return converter2.CanConvertFrom(sourceType);
        }
    }


    public static class AsyncHelper
    {
        public static TResult RunSync<TResult>(Func<Task<TResult>> asyncFunc)
        {
            return Task.Run(asyncFunc).GetAwaiter().GetResult();
        }

        public static void RunSync(Func<Task> asyncAction)
        {
            Task.Run(asyncAction).GetAwaiter().GetResult();
        }
    }

}


