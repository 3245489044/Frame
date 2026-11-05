
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Bus;

namespace Modules.EventFramework.Bus.Decorators;

public class EnhancedEventBusDecorator : EventBusCoreDecoratorBase
{
    public Func<string, object?, bool>? Filter { get; set; }

    public Func<string, object?, object?>? DataTransformer { get; set; }

    public Action<string, object?>? OnPublish
    {
        get => _onPublish;
        set
        {
            _onPublish = value;
            _onPublishManuallySet = true;
        }
    }
    private Action<string, object?>? _onPublish;

    public Action<string>? Logger
    {
        get => _logger;
        set
        {
            _logger = value;
            if (!_onPublishManuallySet)
            {
                _onPublish = value != null
                    ? (eventName, data) => value($"[EventBus] {eventName}: {data}")
                    : null;
            }
        }
    }
    private Action<string>? _logger;
    private bool _onPublishManuallySet;

    public EnhancedEventBusDecorator(IEventBusCore inner) : base(inner)
    {
    }

    public override void Publish(string eventName, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        var processedData = ApplyEnhancements(eventName, data);
        if (processedData == null)
            return;

        Inner.Publish(eventName, processedData);
    }

    public override void Publish<TEvent>(TEvent @event)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        var eventName = typeof(TEvent).Name;
        var processedData = ApplyEnhancements(eventName, @event);
        if (processedData == null)
            return;

        if (processedData is TEvent typedData)
            Inner.Publish(typedData);
        else
            Inner.Publish(eventName, processedData);
    }

    private object? ApplyEnhancements(string eventName, object? data)
    {
        if (Filter != null && !Filter(eventName, data))
            return null;

        var transformedData = DataTransformer != null
            ? DataTransformer(eventName, data)
            : data;

        OnPublish?.Invoke(eventName, transformedData);

        return transformedData;
    }
}
