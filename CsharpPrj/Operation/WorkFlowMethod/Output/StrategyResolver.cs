
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
        if (!string.IsNullOrWhiteSpace(mode))
        {
            // P0-6：显式声明了未知 mode ⇒ 显式失败；不得回落默认/任意策略。
            return _writeStrategies.TryGetValue(mode, out var strategy)
                ? (strategy, true)
                : (MissingWriteStrategy.Instance, false);
        }

        return _writeStrategies.TryGetValue(_defaultWriteMode, out var defaultStrategy)
            ? (defaultStrategy, false)
            : (MissingWriteStrategy.Instance, false);
    }

    public (IReadStrategy<InputAdapter> Strategy, bool HasExplicitConfig) ResolveRead(InputAdapter? adapter)
    {
        var mode = adapter?.Mode;
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return _readStrategies.TryGetValue(mode, out var strategy)
                ? (strategy, true)
                : (MissingReadStrategy.Instance, false);
        }

        return _readStrategies.TryGetValue(_defaultReadMode, out var defaultStrategy)
            ? (defaultStrategy, false)
            : (MissingReadStrategy.Instance, false);
    }
}

/// <summary>未绑定写策略：显式失败（禁静默写回）。</summary>
public sealed class MissingWriteStrategy : IWriteStrategy<OutputAdapter>
{
    public static readonly MissingWriteStrategy Instance = new();

    public void Write(object target, object? value, OutputAdapter? adapter)
        => throw new InvalidOperationException(
            $"WIRE_HANDLER_UNBOUND: 未注册的输出模式 '{adapter?.Mode ?? "(null)"}'");
}

/// <summary>未绑定读策略：显式失败（禁静默读取）。</summary>
public sealed class MissingReadStrategy : IReadStrategy<InputAdapter>
{
    public static readonly MissingReadStrategy Instance = new();

    public object? Read(object source, InputAdapter? adapter)
        => throw new InvalidOperationException(
            $"WIRE_HANDLER_UNBOUND: 未注册的输入模式 '{adapter?.Mode ?? "(null)"}'");
}
