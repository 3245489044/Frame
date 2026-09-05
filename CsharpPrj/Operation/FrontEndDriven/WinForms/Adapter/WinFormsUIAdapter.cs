#nullable enable

using System;
using System.Collections.Generic;
using CSG.Unified;
using ECS.Bridge;
using FrontEndDriven.Abstractions;
using FrontEndDriven.Protocol;

namespace FrontEndDriven.WinForms.Adapter;

/// <summary>
/// WinForms 只读 UI 适配器。并行投影写路径（ApplyCanonical/ApplyPropertyChanges）已随 UIReconciler
/// 一并删除——物理控件属性写入唯一走 WinFormApplier 快照内核（SingleImplementation）。
/// 本类仅保留只读辅助：GetSize（统一 GetWidth/GetHeight 组合入口）与 IConfiguration 快照（ToCanonical）。
/// 依赖直接注入 ControlPool（唯一控件事实源），删除 ControlMapAdapter/IControlMap 薄适配层。
/// </summary>
public sealed class WinFormsUIAdapter : IConfiguration
{
    private readonly ControlPool _controlPool;
    private readonly IControlAdapter _controlAdapter;

    public WinFormsUIAdapter(ControlPool controlPool, IControlAdapter controlAdapter)
    {
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _controlAdapter = controlAdapter ?? throw new ArgumentNullException(nameof(controlAdapter));
    }

    public string ConfigType => "WinForms.UI";

    public string SchemaVersion => "1.0";

    public CanonicalForm ToCanonical()
    {
        var builder = new CanonicalFormBuilder()
            .WithMetadata("configType", ConfigType)
            .WithMetadata("schemaVersion", SchemaVersion)
            .WithMetadata("protocolVersion", CsgProtocol.CurrentVersion.ToString());

        foreach (var entity in _controlPool.GetAllEntities())
        {
            var control = _controlPool.GetControl(entity);
            if (control == null || _controlAdapter.IsDisposed(control))
                continue;

            builder.AddNode(
                id: entity.ToString(),
                label: _controlAdapter.GetName(control) ?? entity.ToString(),
                type: CanonicalNodeType.Control,
                properties: BuildNodeProperties(control));
        }

        return builder.Build();
    }

    private Dictionary<string, object?> BuildNodeProperties(object control)
    {
        var props = new Dictionary<string, object?>
        {
            [UIProtocol.Prop_ControlType] = control.GetType().Name,
            [UIProtocol.Prop_Left] = _controlAdapter.GetLeft(control),
            [UIProtocol.Prop_Top] = _controlAdapter.GetTop(control),
            [UIProtocol.Prop_Width] = _controlAdapter.GetWidth(control),
            [UIProtocol.Prop_Height] = _controlAdapter.GetHeight(control),
            [UIProtocol.Prop_Text] = _controlAdapter.GetText(control),
            [UIProtocol.Prop_Enabled] = _controlAdapter.GetEnabled(control),
            [UIProtocol.Prop_Visible] = _controlAdapter.GetVisible(control),
        };

        var name = _controlAdapter.GetName(control);
        if (name != null)
            props[UIProtocol.Prop_Name] = name;

        return props;
    }

    /// <summary>尺寸读取辅助（R3）：统一 GetWidth/GetHeight 组合入口，消除多处独立调用的重复。</summary>
    internal (int Width, int Height) GetSize(object control)
        => (_controlAdapter.GetWidth(control), _controlAdapter.GetHeight(control));
}
