
#nullable enable
using UniversalMiddleware.Abstractions;
using ContainerEventManager = UniversalMiddleware.Container.ContainerEventManager;

namespace UniversalMiddleware.Container.Events;

public enum PropagationDirection
{
    Bubble,
    Tunnel,
    Direct
}

public class EventPropagationArgs : EventArgs
{
    public string EventName { get; }

    public object? Data { get; }

    public ContainerNode Source { get; }

    public ContainerNode Current { get; }

    public PropagationDirection Direction { get; }

    public bool Handled { get; set; }

    public EventPropagationArgs(string eventName, object? data, ContainerNode source, ContainerNode current, PropagationDirection direction)
    {
        EventName = eventName;
        Data = data;
        Source = source;
        Current = current;
        Direction = direction;
    }
}

public class EventPropagator
{
    private readonly ContainerEventManager _eventManager;

    public EventPropagator(ContainerEventManager eventManager)
    {
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
    }

    public async Task BubbleAsync(ContainerNode source, string eventName, object? data = null)
    {
        var current = source;
        while (current != null)
        {
            var args = new EventPropagationArgs(eventName, data, source, current, PropagationDirection.Bubble);
            await _eventManager.PublishAsync($"{current.Name}:{eventName}", args);

            if (args.Handled)
                break;

            current = current.Parent;
        }
    }

    public async Task TunnelAsync(ContainerNode root, string eventName, object? data = null)
    {
        foreach (var node in root.TraversePreOrder())
        {
            var args = new EventPropagationArgs(eventName, data, root, node, PropagationDirection.Tunnel);
            await _eventManager.PublishAsync($"{node.Name}:{eventName}", args);

            if (args.Handled)
                break;
        }
    }

    public async Task DirectAsync(ContainerNode node, string eventName, object? data = null)
    {
        var args = new EventPropagationArgs(eventName, data, node, node, PropagationDirection.Direct);
        await _eventManager.PublishAsync($"{node.Name}:{eventName}", args);
    }
}
