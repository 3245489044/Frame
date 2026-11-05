
namespace ParameterResolver.Strategies;

public abstract class StrategyResolver<TStrategy, TAdapter>
    where TStrategy : class
    where TAdapter : class
{
    private readonly Dictionary<string, TStrategy> _strategies;
    private readonly string _defaultMode;

    protected StrategyResolver(string defaultMode = "text")
    {
        _strategies = new Dictionary<string, TStrategy>(StringComparer.OrdinalIgnoreCase);
        _defaultMode = defaultMode;
        RegisterDefaults();
    }

    protected abstract void RegisterDefaults();

    public void Register(string mode, TStrategy strategy)
    {
        _strategies[mode] = strategy;
    }

    protected void AddDefault(string mode, TStrategy strategy)
    {
        _strategies[mode] = strategy;
    }

    public (TStrategy Strategy, bool HasExplicitConfig) Resolve(TAdapter? adapter)
    {
        var mode = adapter != null ? GetMode(adapter) : null;
        if (mode != null && _strategies.TryGetValue(mode, out var strategy))
        {
            return (strategy, true);
        }

        return (_strategies.TryGetValue(_defaultMode, out var defaultStrategy)
            ? defaultStrategy
            : _strategies.Values.First(), false);
    }

    protected abstract string? GetMode(TAdapter adapter);
}
