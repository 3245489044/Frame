
using System;
using System.Collections.Generic;
using System.Linq;
using ParameterResolver.Abstractions;

namespace FrontEndDriven.WinForms.Applier;

public sealed class WinFormSnapshot
{
    public List<ControlDescriptor> Controls { get; set; } = [];

    public List<ChildRelation> Relations { get; set; } = [];
}

public sealed class ControlDescriptor : IMetadataProvider<ControlDescriptor>
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;

    public string? FontFamily { get; set; }
    public float FontSize { get; set; }
    public bool FontBold { get; set; }
    public bool FontItalic { get; set; }
    public bool FontUnderline { get; set; }

    public string? ForeColor { get; set; }
    public string? BackColor { get; set; }

    public int BorderWidth { get; set; }
    public string? BorderColor { get; set; }

    // M2·布局意图化：原生 Dock/Anchor 意图（由 LayoutIntentComponent 投影；设置后不写像素 Bounds）
    public bool HasDock { get; set; }
    public string? Dock { get; set; }
    public string? Anchor { get; set; }

    public int? SplitterDistance { get; set; }

    public int? SelectedIndex { get; set; }

    public List<string>? Items { get; set; }

    public List<EventBindingDescriptor>? EventBindings { get; set; }

    public string? ExecutionResult { get; set; }

    public bool ExecutionSuccess { get; set; }

    public string? ExecutionError { get; set; }


    static IReadOnlyList<ColumnDefinition<ControlDescriptor>> IMetadataProvider<ControlDescriptor>.GetColumns()
        => new ColumnDefinition<ControlDescriptor>[]
    {
        new("ID",        c => c.Id),
        new("Type",      c => c.Type),
        new("Left",      c => c.Left),
        new("Top",       c => c.Top),
        new("Width",     c => c.Width),
        new("Height",    c => c.Height),
        new("Text",      c => c.Text),
        new("Visible",   c => c.Visible),
        new("Enabled",   c => c.Enabled),
        new("Font",      c => c.FontFamily != null ? $"{c.FontFamily}/{c.FontSize}" : null),
        new("ForeColor", c => c.ForeColor),
        new("BackColor", c => c.BackColor),
        new("Splitter",  c => c.SplitterDistance),
        new("Events",    c => c.EventBindings != null
            ? string.Join("|", c.EventBindings.Select(e => $"{e.EventName}:{e.FunctionName}"))
            : null),
        new("ExecResult", c => c.ExecutionResult, SkipIfNull: true),
        new("ExecError",  c => c.ExecutionError, SkipIfNull: true),
    };
}

public sealed class EventBindingDescriptor
{
    public string EventName { get; set; } = "";

    public string FunctionName { get; set; } = "";
}

public sealed class ChildRelation : IMetadataProvider<ChildRelation>
{
    public string ParentId { get; set; } = "";

    public string ChildId { get; set; } = "";

    public string? TargetPanel { get; set; }

    /// <summary>0 基兄弟顺序（来自声明数组下标，经 SiblingOrderComponent 建模）。挂载按 (ParentId, OrderIndex) 稳定排序，保证顺序敏感容器（Tab/分栏/列表）与声明一致。</summary>
    public int OrderIndex { get; set; }

    static IReadOnlyList<ColumnDefinition<ChildRelation>> IMetadataProvider<ChildRelation>.GetColumns()
        => new ColumnDefinition<ChildRelation>[]
    {
        new("ParentId",    r => r.ParentId),
        new("ChildId",     r => r.ChildId),
        new("TargetPanel", r => r.TargetPanel),
        new("Order",       r => r.OrderIndex),
    };
}

/// <summary>
/// 快照应用结果（P7：结果契约闭合；M2：挂载判据物理闭环）。
/// 语义分级：ControlsProcessed=快照控件数；RelationsApplied=成功挂载数（**经物理核验**：AddControl 后二次确认
/// 已入宿主目标子集合 TabPages/Controls/SplitterPanel，杜绝「调用即成功」）；RelationsSkipped=孤儿/挂载失败/物理未入宿主数。
/// AllMounted=false 即「控件对象存在但未挂载到可见容器」——上层（EcsGate/审计/validate）以此为挂载失败判据，
/// 消除「日志全绿但 UI 空」的假阳性（validate 存在性 ≠ 控件已挂载可见）。
/// </summary>
public sealed class ApplyResult
{
    public int ControlsProcessed { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    /// <summary>控件级跳过（Entity 解析失败 / 创建失败），非关系挂载跳过。</summary>
    public int ControlsSkipped { get; set; }

    public int RelationsProcessed { get; set; }

    /// <summary>成功挂载数——仅含「AddControl 返回 true 且物理核验（Contains）通过」的关系（M2 物理闭环）。</summary>
    public int RelationsApplied { get; set; }

    /// <summary>关系挂载跳过数（父/子未找到、目标面板无效、AddControl 失败、或物理核验未入宿主）。</summary>
    public int RelationsSkipped { get; set; }

    /// <summary>跳过明细（分类：ParseFailed / CreateFailed / ParentNotFound / ChildNotFound / MountFailed / PhysicallyNotMounted），供审计与断言定位。</summary>
    public List<string> SkipReasons { get; } = [];

    /// <summary>S3：ECS_MISMATCH 缺陷数（SyncActualContainerSizes 每次 Apply 后审计回填；ComboBox DropDownList 平台豁免不计）。</summary>
    public int MismatchCount { get; set; }

    /// <summary>
    /// M2 物理闭环判定：任何关系挂载失败（含物理核验未入宿主）或控件级失败 → false。
    /// 配置误嵌套时因挂载/核验失败计入 skipped，此处真实反映「控件对象存在但未挂载到可见容器」。
    /// </summary>
    public bool AllMounted => RelationsSkipped == 0 && ControlsSkipped == 0;

    public string ToSummaryText()
        => $"controls={ControlsProcessed}(+{CreatedCount}/~{UpdatedCount}/!{ControlsSkipped}), relations={RelationsApplied}/{RelationsProcessed} mounted, skipped={RelationsSkipped}, mismatch={MismatchCount}";
}
