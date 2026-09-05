
using System.Text.Json.Serialization;

namespace TConfig;

public class ViewConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public LayoutConfig Layout { get; set; } = new();
    public List<ControlConfig> Controls { get; set; } = [];
    public DataSourceConfig? DataSource { get; set; }
    public EventConfig? Events { get; set; }
}

public class LayoutConfig
{
    public LayoutType Type { get; set; } = LayoutType.Flow;
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public PaddingConfig Padding { get; set; } = new();
    public SpacingConfig Spacing { get; set; } = new();
}

public enum LayoutType
{
    Flow,
    Grid,
    Table,
    Stack,
    Dock
}

public class PaddingConfig
{
    public int Left { get; set; } = 5;
    public int Top { get; set; } = 5;
    public int Right { get; set; } = 5;
    public int Bottom { get; set; } = 5;
}

public class SpacingConfig
{
    public int Horizontal { get; set; } = 5;
    public int Vertical { get; set; } = 5;
}
