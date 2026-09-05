
#nullable enable
using TConfig;
using UniversalLayoutFramework;
using UniversalLayoutMath;

namespace UniversalMiddleware.ConfigurationModels.Builders;





public class ControlConfigBuilder
{
    private readonly ControlConfig _control = new();
    private readonly List<ControlConfig> _children = new();

    public ControlConfigBuilder(string name)
    {
        _control.Name = name;
    }

    public ControlConfigBuilder OfType(string type)
    {
        _control.Type = type;
        return this;
    }

    public ControlConfigBuilder WithText(string text)
    {
        _control.Text = text;
        return this;
    }

    public ControlConfigBuilder AtPosition(int x, int y)
    {
        _control.X = x;
        _control.Y = y;
        return this;
    }

    public ControlConfigBuilder WithSize(int width, int height)
    {
        _control.Width = width;
        _control.Height = height;
        return this;
    }

    public ControlConfigBuilder WithDock(string dock)
    {
        _control.Dock = dock;
        return this;
    }

    public ControlConfigBuilder WithAnchor(string anchor)
    {
        _control.Anchor = anchor;
        return this;
    }

    public ControlConfigBuilder WithLayout(double left, double top, double width, double height)
    {
        _control.Layout = new TConfig.LayoutPosition

        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
        return this;
    }


    public ControlConfigBuilder WithStyle(Action<StyleConfig> configure)
    {
        _control.Style = new StyleConfig();
        configure(_control.Style);
        return this;
    }

    public ControlConfigBuilder WithEvents(Action<EventConfig> configure)
    {
        _control.Events = new EventConfig();
        configure(_control.Events);
        return this;
    }

    public ControlConfigBuilder WithDataBinding(string dataSource, string dataMember, string formatString = "")
    {
        _control.DataBinding = new DataBindingConfig
        {
            DataSource = dataSource,
            DataMember = dataMember,
            FormatString = formatString
        };
        return this;
    }


    public ControlConfigBuilder AddChild(string name, Action<ControlConfigBuilder> configure)
    {
        var builder = new ControlConfigBuilder(name);
        configure(builder);
        _children.Add(builder.Build());
        return this;
    }

    public ControlConfigBuilder WithStackLayout(string direction, int spacing = 5)
    {
        _control.StackLayout = new TConfig.StackLayoutConfig
        {
            Direction = direction,
            Spacing = spacing
        };
        return this;
    }




    public ControlConfigBuilder AsContainer(string sourceType = "static", bool autoLoad = true)
    {
        _control.Container = new ContainerConfig
        {
            SourceType = sourceType,
            AutoLoad = autoLoad
        };
        return this;
    }

    public ControlConfigBuilder Enabled(bool enabled)
    {
        _control.Enabled = enabled;
        return this;
    }

    public ControlConfigBuilder WithBackColor(string color)
    {
        _control.BackColor = color;
        return this;
    }

    public ControlConfigBuilder WithForeColor(string color)
    {
        _control.ForeColor = color;
        return this;
    }

    public ControlConfig Build()
    {
        if (_children.Count > 0)
            _control.Controls = _children;
        return _control;
    }
}
