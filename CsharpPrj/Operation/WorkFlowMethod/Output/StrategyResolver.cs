
using ParameterResolver.Models;
using ParameterResolver.Strategies.Abstractions;
using WorkFlowMethod.Output.Strategies;

namespace WorkFlowMethod.Output;

public class StrategyResolver
{
    private readonly Dictionary<string, IWriteStrategy<OutputAdapter>> _writeStrategies;
    private readonly Dictionary<string, IReadStrategy<InputAdapter>> _readStrategies;
    private readonly string _defaultWriteMode;
    private readonly string _defaultReadMode;

    public StrategyResolver()
    {
        _writeStrategies = new Dictionary<string, IWriteStrategy<OutputAdapter>>(StringComparer.OrdinalIgnoreCase);
        _readStrategies = new Dictionary<string, IReadStrategy<InputAdapter>>(StringComparer.OrdinalIgnoreCase);
        _defaultWriteMode = "text";
        _defaultReadMode = "text";
        RegisterDefaults();
    }

    private void RegisterDefaults()
    {
        _writeStrategies["text"] = new TextWriteStrategy();
        _writeStrategies["list"] = new ListWriteStrategy();
        _writeStrategies["items"] = new ListWriteStrategy();

        _readStrategies["text"] = new TextReadStrategy();
        _readStrategies["selectedItem"] = new SelectedItemReadStrategy();
        _readStrategies["selectedItems"] = new SelectedItemReadStrategy();
        _readStrategies["value"] = new TextReadStrategy();
    }

    public void RegisterWriteStrategy(string mode, IWriteStrategy<OutputAdapter> strategy)
    {
        _writeStrategies[mode] = strategy;
    }

    public void RegisterReadStrategy(string mode, IReadStrategy<InputAdapter> strategy)
    {
        _readStrategies[mode] = strategy;
    }

    public (IWriteStrategy<OutputAdapter> Strategy, bool HasExplicitConfig) ResolveWrite(OutputAdapter? adapter)
    {
        var mode = adapter?.Mode;
        if (mode != null && _writeStrategies.TryGetValue(mode, out var strategy))
        {
            return (strategy, true);
        }

        return (_writeStrategies.TryGetValue(_defaultWriteMode, out var defaultStrategy)
            ? defaultStrategy
            : _writeStrategies.Values.First(), false);
    }

    public (IReadStrategy<InputAdapter> Strategy, bool HasExplicitConfig) ResolveRead(InputAdapter? adapter)
    {
        var mode = adapter?.Mode;
        if (mode != null && _readStrategies.TryGetValue(mode, out var strategy))
        {
            return (strategy, true);
        }

        return (_readStrategies.TryGetValue(_defaultReadMode, out var defaultStrategy)
            ? defaultStrategy
            : _readStrategies.Values.First(), false);
    }
}
