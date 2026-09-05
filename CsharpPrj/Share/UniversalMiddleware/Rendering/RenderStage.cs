
#nullable enable
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Rendering;

public abstract class RenderStageBase : IRenderStage
{
    public abstract string Name { get; }

    public abstract int Order { get; }

    public abstract Task ExecuteAsync(IRenderSession session);
}

public class CreateStage : RenderStageBase
{
    private readonly Func<IRenderSession, Task> _createAction;

    public override string Name => "Create";
    public override int Order => 0;

    public CreateStage(Func<IRenderSession, Task> createAction)
    {
        _createAction = createAction;
    }

    public override async Task ExecuteAsync(IRenderSession session)
    {
        await session.ExecuteStageAsync(Name, () => _createAction(session));
    }
}

public class LayoutStage : RenderStageBase
{
    private readonly Func<IRenderSession, Task> _layoutAction;

    public override string Name => "Layout";
    public override int Order => 1;

    public LayoutStage(Func<IRenderSession, Task> layoutAction)
    {
        _layoutAction = layoutAction;
    }

    public override async Task ExecuteAsync(IRenderSession session)
    {
        await session.ExecuteStageAsync(Name, () => _layoutAction(session));
    }
}

public class BindEventsStage : RenderStageBase
{
    private readonly Func<IRenderSession, Task> _bindAction;

    public override string Name => "BindEvents";
    public override int Order => 2;

    public BindEventsStage(Func<IRenderSession, Task> bindAction)
    {
        _bindAction = bindAction;
    }

    public override async Task ExecuteAsync(IRenderSession session)
    {
        await session.ExecuteStageAsync(Name, () => _bindAction(session));
    }
}
