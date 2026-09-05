using System.Collections.Concurrent;
using TConfig.Controls;

namespace WinFormsValueContract;

/// <summary>
/// 控件值契约注册表（值语义契约层的注册中心，类型差异唯一落点）。
/// 形态仿 FrontEndDriven.ControlStrategyRegistry（既有注册表模式）；内置契约在静态构造时注册，
/// 扩展点 = <see cref="Register"/>（新增控件类型 = 注册一条契约，消费方零改动）。
/// </summary>
public static class ValueContractRegistry
{
    private static readonly ConcurrentDictionary<string, ControlValueContract> _contracts = new(StringComparer.OrdinalIgnoreCase);

    static ValueContractRegistry()
    {
        BuiltInValueContracts.RegisterAll(_contracts);
    }

    /// <summary>注册契约（按类型名覆盖）。</summary>
    public static void Register(ControlValueContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _contracts[contract.ControlType] = contract;
    }

    /// <summary>按类型名解析契约。</summary>
    public static ControlValueContract? Get(string controlType)
    {
        if (string.IsNullOrWhiteSpace(controlType)) return null;
        return _contracts.TryGetValue(controlType, out var contract) ? contract : null;
    }

    /// <summary>按运行时类型解析契约（类型名精确匹配 → 基类回退）。</summary>
    public static ControlValueContract? GetForType(Type controlType)
    {
        ArgumentNullException.ThrowIfNull(controlType);

        if (Get(controlType.Name) is { } exact) return exact;

        var baseType = controlType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (Get(baseType.Name) is { } byBase) return byBase;
            baseType = baseType.BaseType;
        }

        return null;
    }

    /// <summary>清空全部契约（测试隔离用）。</summary>
    public static void Clear() => _contracts.Clear();
}
