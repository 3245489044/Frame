
using Modules.EventFramework.Abstractions;

namespace Modules.EventFramework.Services;

public interface IValueWriter
{
    Task WriteAsync(IEventSourceNode source, string outputTarget, object? value);
}

public class DefaultValueWriter : IValueWriter
{
    public Task WriteAsync(IEventSourceNode source, string outputTarget, object? value)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(outputTarget))
            throw new ArgumentException("输出目标不能为空", nameof(outputTarget));

        if (string.Equals(outputTarget, ((IEventSource)source).Name, StringComparison.OrdinalIgnoreCase))
        {
            if (source.CanWrite)
            {
                source.SetValue(value);
            }
            return Task.CompletedTask;
        }

        var target = source.FindChild(outputTarget);
        if (target is IValueContainer container && container.CanWrite)
        {
            container.SetValue(value);
        }

        return Task.CompletedTask;
    }
}
