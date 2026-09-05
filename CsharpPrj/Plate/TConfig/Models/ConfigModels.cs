
using System.Text.Json.Serialization;

namespace TConfig;

public class FormConfig
{
    public string? Title { get; set; }
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public string? StartPosition { get; set; } = "CenterScreen";
    public bool MaximizeBox { get; set; } = true;
    public bool MinimizeBox { get; set; } = true;
    public bool ShowIcon { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = true;
    public FormBorderStyle BorderStyle { get; set; } = FormBorderStyle.Sizable;

    public string WindowState { get; set; } = "Normal";
    public string IconPath { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsWidthSet { get; set; }

    [JsonIgnore]
    public bool IsHeightSet { get; set; }

    [JsonIgnore]
    public bool IsMaximizeBoxSet { get; set; }

    [JsonIgnore]
    public bool IsMinimizeBoxSet { get; set; }

    [JsonIgnore]
    public bool IsWindowStateSet { get; set; }
}

public enum FormBorderStyle
{
    None,
    FixedSingle,
    Fixed3D,
    FixedDialog,
    Sizable,
    FixedToolWindow,
    SizableToolWindow
}

public class NavigationConfig
{
    public string? Type { get; set; } = "Tab";
    public bool Visible { get; set; } = true;
    public string? Position { get; set; } = "Top";

    public string DefaultView { get; set; } = string.Empty;
    public List<NavigationItem> Items { get; set; } = [];
    public bool ShowNavigationBar { get; set; } = true;
    public string NavigationBarPosition { get; set; } = "Top";
    public List<NavigationButtonConfig> Buttons { get; set; } = [];
}

public class NavigationItem
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public List<NavigationItem> Children { get; set; } = [];
}

public class NavigationButtonConfig
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int ViewIndex { get; set; }
    public bool IsDefault { get; set; }
}

public class DataBindingConfig
{
    public string? Source { get; set; }
    public string? Target { get; set; }
    public string? Property { get; set; }
    public string? Format { get; set; }
    public bool TwoWay { get; set; }

    public string DataSource { get; set; } = string.Empty;
    public string DataMember { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public string UpdateMode { get; set; } = "OnPropertyChanged";
    public bool ReadOnly { get; set; } = false;
}

public class StyleConfig
{
    public string? FontName { get; set; }
    public float FontSize { get; set; } = 9f;
    public string? ForeColor { get; set; }
    public string? BackColor { get; set; }

    public string FontFamily { get; set; } = "Microsoft Sans Serif";
    public string FontStyle { get; set; } = "Regular";
    public string BorderColor { get; set; } = "#CCCCCC";
    public int BorderWidth { get; set; } = 1;
    public string BorderStyle { get; set; } = "FixedSingle";
    [JsonIgnore]
    public string? StyleReference { get; set; }
}

public class DataLoadConfig
{
    public string? Type { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
}

public class LayoutPosition
{
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;

    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public double Width { get; set; } = -1;
    public double Height { get; set; } = -1;
    public double Bottom { get; set; } = -1;
    public double Right { get; set; } = -1;
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    public int MinWidth { get; set; }
    public int MinHeight { get; set; }
    public int MaxWidth { get; set; }
    public int MaxHeight { get; set; }

    // M2·布局意图化：可选意图字段（缺省=proportional 存量兼容）。
    // "proportional"：比例布局（现行为）；"dock"：原生 Dock 接管；"anchor"：原生 Anchor 接管；"fixed"：像素直定位。
    public string? Intent { get; set; }

    /// <summary>原生 Dock 枚举名（Fill/Top/Bottom/Left/Right/None），Intent="dock" 时生效；缺省 Fill。</summary>
    public string? Dock { get; set; }

    /// <summary>原生 Anchor 组合（如 "Left,Right,Top,Bottom"），Intent="anchor" 时生效。</summary>
    public string? Anchor { get; set; }
}

public class DataSourceConfig
{
    public string? Type { get; set; }
    public string? Source { get; set; }
    public string? DisplayMember { get; set; }
    public string? ValueMember { get; set; }
}

public class ColumnConfig
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public int Width { get; set; } = 100;
}

public class TabPageConfig
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public List<ControlConfig>? Controls { get; set; }
}

public class ListViewColumnConfig
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public int Width { get; set; } = 100;
    public string? TextAlign { get; set; } = "Left";
}

public class StackLayoutConfig
{
    public string? Direction { get; set; } = "Vertical";
    public int Spacing { get; set; } = 5;
    public bool Wrap { get; set; }
}

public class ContainerConfig
{
    public string? Type { get; set; }
    public string? Name { get; set; }

    public string SourceType { get; set; } = "dynamic";
    public bool AutoLoad { get; set; } = true;
    public string? DefaultConfig { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StateScope { get; set; }
}