using Rge.Unified.Systems;

namespace ExecutionFramework.Systems;

public sealed class IntentSystemRouter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IIntentSystem> _systems = new(StringComparer.OrdinalIgnoreCase);

    public IntentSystemRouter()
    {
    }

    public IntentSystemRouter(IEnumerable<IIntentSystem> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);
        foreach (var system in systems)
            Register(system);
    }

    public void Register(IIntentSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        lock (_gate)
            _systems[system.StepType] = system;
    }

    public IIntentSystem? Route(string? stepType)
    {
        if (string.IsNullOrWhiteSpace(stepType))
            return null;

        lock (_gate)
            return _systems.TryGetValue(stepType, out var system) ? system : null;
    }

    public IEnumerable<string> RegisteredTypes
    {
        get
        {
            lock (_gate)
                return _systems.Keys.ToList();
        }
    }

    public static IntentSystemRouter CreateDefault(
        TConfig.IFunctionExecutor? functionExecutor = null)
    {
        var router = new IntentSystemRouter();

        var functionSystem = new FunctionSystem(functionExecutor);
        var conditionSystem = new ConditionSystem(functionExecutor, router);
        var loopSystem = new LoopSystem(router);
        var parallelSystem = new ParallelSystem(router);

        router.Register(functionSystem);
        router.Register(conditionSystem);
        router.Register(loopSystem);
        router.Register(parallelSystem);

        return router;
    }
}