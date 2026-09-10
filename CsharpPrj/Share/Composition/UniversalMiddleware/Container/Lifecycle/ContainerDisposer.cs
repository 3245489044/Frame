
#nullable enable
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Container.Lifecycle;

public class ContainerDisposer
{
    private readonly ContainerLifecycle _lifecycle;
    private readonly ContainerNode _node;

    public ContainerDisposer(ContainerLifecycle lifecycle, ContainerNode node)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _node = node ?? throw new ArgumentNullException(nameof(node));
    }

    public async Task DisposeAsync()
    {
        if (_lifecycle.IsDisposed)
            return;

        if (!_lifecycle.TryTransitionTo(ContainerLifecycleState.Disposing))
            return;

        foreach (var child in _node.Children.ToList())
        {
            if (child.Container is IContainer container)
            {
                var childDisposer = new ContainerDisposer(
                    container.Lifecycle,
                    child);
                await childDisposer.DisposeAsync();
            }
            else
            {
                try
                {
                    child.Container.ClearContent();
                }
                catch
                {
                }
                child.Parent?.RemoveChild(child.Name);
            }
        }

        _node.Parent?.RemoveChild(_node.Name);

        try
        {
            _node.Container.ClearContent();
        }
        catch
        {
        }

        _lifecycle.TryTransitionTo(ContainerLifecycleState.Disposed);
    }

    public async Task DisposeSelfAsync()
    {
        if (_lifecycle.IsDisposed)
            return;

        if (!_lifecycle.TryTransitionTo(ContainerLifecycleState.Disposing))
            return;

        _node.Parent?.RemoveChild(_node.Name);

        try
        {
            _node.Container.ClearContent();
        }
        catch
        {
        }

        _lifecycle.TryTransitionTo(ContainerLifecycleState.Disposed);
        await Task.CompletedTask;
    }
}
