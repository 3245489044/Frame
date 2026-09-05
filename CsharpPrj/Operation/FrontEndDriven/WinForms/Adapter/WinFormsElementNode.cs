#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TConfig.Controls;
using UiBackend.Abstractions;

namespace FrontEndDriven.WinForms.Adapter;

/// <summary>
/// WinForms 后端节点适配器——包装物理 <see cref="Control"/> 实现 <see cref="IUiElementNode"/>：
/// 值读写委托值语义契约层（<see cref="IControlService"/>，类型差异由 ValueContractRegistry 承载），
/// 事件触发与定位直接作用于物理控件。供统一节点访问器（脚本命令执行器 / 事件分派）在 WinForms 后端的落点。
/// </summary>
public sealed class WinFormsElementNode : IUiElementNode
{
    private readonly Control _control;
    private readonly IControlService _valueService;
    private readonly List<IUiElementNode> _children = [];

    public WinFormsElementNode(Control control, IControlService valueService, string? typeName = null)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _valueService = valueService ?? throw new ArgumentNullException(nameof(valueService));
        TypeName = string.IsNullOrWhiteSpace(typeName) ? control.GetType().Name : typeName!;
        if (control.Controls.Count > 0)
            _children.AddRange(control.Controls.Cast<Control>().Select(c => new WinFormsElementNode(c, valueService)));
    }

    public string Name => _control.Name ?? string.Empty;

    public string TypeName { get; }

    public object? GetValue() => _valueService.GetValue(_control);

    public void SetValue(object? value) => _valueService.SetValue(_control, value);

    public void Invoke(string eventName, object? payload)
    {
        if (string.Equals(eventName, "click", StringComparison.OrdinalIgnoreCase)
            && _control.GetType().GetMethod("PerformClick") is { } performClick)
            performClick.Invoke(_control, null);
    }

    public void SetBounds(int x, int y, int width, int height)
        => _control.SetBounds(x, y, width, height);

    public (int X, int Y, int Width, int Height) GetBounds()
        => (_control.Left, _control.Top, _control.Width, _control.Height);

    public IEnumerable<IUiElementNode> Children => _children;
}
