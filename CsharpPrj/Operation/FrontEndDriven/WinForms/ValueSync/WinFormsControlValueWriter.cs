using System;
using System.Collections.Generic;
using FrontEndDriven.Utils;
using WinFormsValueContract;

namespace FrontEndDriven.Implementation;

/// <summary>
/// WinForms 控件值写入器（阶段E·收敛重复）：作为 <see cref="TConfig.Controls.IControlValueAccessor"/> 薄适配器，
/// 内置控件值读写全部委托值语义契约层（ValueContractRegistry / ControlValueBridge），
/// 删除本类型手写内置适配器表（原 RegisterTextControlAdapter/ListView/ComboBox/CheckBox/DateTimePicker/...
/// 与 ParameterResolver 的 BuiltInValueContracts 重复）。本类仅保留宿主角色：
/// 用户自定义适配器（<see cref="RegisterAdapter"/>）优先，其余类型走契约层（单一真源）。
/// </summary>
public class WinFormsControlValueWriter : TConfig.Controls.IControlValueAccessor
{
    private readonly Dictionary<Type, AdapterEntry> _adapterRegistry = new();
    private readonly TConfig.Controls.IControlService _contractService;

    private record AdapterEntry(Func<object, object?> Reader, Action<object, object?> Setter);

    /// <summary>
    /// 构造：注入契约服务；无注入时回退默认后端（<see cref="ControlValueBridge"/>）。
    /// </summary>
    public WinFormsControlValueWriter(TConfig.Controls.IControlService? contractService = null)
    {
        _contractService = contractService ?? new ControlValueBridge();
    }

    public object? GetValue(object control)
    {
        if (control == null) return null;
        if (FindAdapter(control.GetType()) is { } entry)
            return UiThreadHelper.DispatchIfNeeded(control, () => entry.Reader(control));
        return UiThreadHelper.DispatchIfNeeded(control, () => _contractService.GetValue(control));
    }

    public void SetValue(object control, object? value)
    {
        if (control == null) return;
        if (FindAdapter(control.GetType()) is { } entry)
        {
            UiThreadHelper.DispatchIfNeeded(control, () => entry.Setter(control, value));
            return;
        }
        UiThreadHelper.DispatchIfNeeded(control, () => _contractService.SetValue(control, value));
    }

    public bool IsSupported(object control)
        => control != null && (FindAdapter(control.GetType()) != null || _contractService.IsSupported(control));

    public void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter)
    {
        if (controlType == null) throw new ArgumentNullException(nameof(controlType));
        if (getter == null) throw new ArgumentNullException(nameof(getter));
        if (setter == null) throw new ArgumentNullException(nameof(setter));
        _adapterRegistry[controlType] = new AdapterEntry(getter, setter);
    }

    public void ClearAdapters() => _adapterRegistry.Clear();

    private AdapterEntry? FindAdapter(Type controlType)
    {
        if (_adapterRegistry.TryGetValue(controlType, out var exact)) return exact;
        var baseType = controlType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_adapterRegistry.TryGetValue(baseType, out var byBase)) return byBase;
            baseType = baseType.BaseType;
        }
        return null;
    }
}
