
#nullable enable
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Rendering.Lifecycle;

public interface IRenderLifecycle
{
    Task OnBeforeRenderAsync(IRenderSession session);

    Task OnAfterRenderAsync(IRenderSession session, RenderCompletedEventArgs result);

    Task OnRenderErrorAsync(IRenderSession session, Exception exception);
}

public abstract class RenderLifecycleBase : IRenderLifecycle
{
    public virtual Task OnBeforeRenderAsync(IRenderSession session) => Task.CompletedTask;
    public virtual Task OnAfterRenderAsync(IRenderSession session, RenderCompletedEventArgs result) => Task.CompletedTask;
    public virtual Task OnRenderErrorAsync(IRenderSession session, Exception exception) => Task.CompletedTask;
}
