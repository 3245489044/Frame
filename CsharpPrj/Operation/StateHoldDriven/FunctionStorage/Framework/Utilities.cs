
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


    public class RetryPolicy

    {
        public int MaxRetries { get; set; } = 3;

        public int BaseDelayMs { get; set; } = 1000;

        public bool UseExponentialBackoff { get; set; } = true;

        public Func<Exception, bool>? RetryCondition { get; set; }

        public Action<int, Exception>? OnRetry { get; set; }

        public static RetryPolicy Default => new()
        {
            MaxRetries = 3,
            BaseDelayMs = 1000,
            UseExponentialBackoff = true,
            RetryCondition = ex => ex is TimeoutException || ex is IOException
        };

        public static RetryPolicy NoRetry => new()
        {
            MaxRetries = 0
        };

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var attempt = 0;
            while (true)
            {
                try
                {
                    return await operation(cancellationToken);
                }
                catch (Exception ex) when (attempt < MaxRetries && (RetryCondition?.Invoke(ex) ?? true))
                {
                    attempt++;
                    OnRetry?.Invoke(attempt, ex);

                    if (attempt >= MaxRetries)
                        throw;

                    var delay = UseExponentialBackoff
                        ? (int)(BaseDelayMs * Math.Pow(2, attempt - 1))
                        : BaseDelayMs;

                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        public T Execute<T>(Func<T> operation)
        {
            var attempt = 0;
            while (true)
            {
                try
                {
                    return operation();
                }
                catch (Exception ex) when (attempt < MaxRetries && (RetryCondition?.Invoke(ex) ?? true))
                {
                    attempt++;
                    OnRetry?.Invoke(attempt, ex);

                    if (attempt >= MaxRetries)
                        throw;

                    var delay = UseExponentialBackoff
                        ? (int)(BaseDelayMs * Math.Pow(2, attempt - 1))
                        : BaseDelayMs;

                    Thread.Sleep(delay);
                }
            }
        }

        public int GetDelayMs(int attempt)
        {
            if (attempt <= 0) return 0;
            return UseExponentialBackoff
                ? (int)(BaseDelayMs * Math.Pow(2, attempt - 1))
                : BaseDelayMs;
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


