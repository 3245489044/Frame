
#nullable enable
using TConfig;

namespace UniversalMiddleware.ConfigurationModels.Builders;


public class UiConfigBuilder
{
    private readonly UIConfiguration _config = new();
    private readonly List<ControlConfigBuilder> _viewBuilders = new();

    public UiConfigBuilder WithFormTitle(string title)
    {
        _config.Form.Title = title;
        return this;
    }

    public UiConfigBuilder WithFormSize(int width, int height)
    {
        _config.Form.Width = width;
        _config.Form.Height = height;
        return this;
    }

    public UiConfigBuilder WithFormStartPosition(string startPosition)
    {
        _config.Form.StartPosition = startPosition;
        return this;
    }

    public UiConfigBuilder AddView(string name, Action<ControlConfigBuilder> configure)
    {
        var builder = new ControlConfigBuilder(name);
        configure(builder);
        _viewBuilders.Add(builder);

        var viewConfig = new ViewConfig
        {
            Name = name,
            Controls = builder.Build().Controls ?? new List<ControlConfig>()
        };
        _config.Views.Add(viewConfig);
        return this;
    }

    public UiConfigBuilder WithNavigation(Action<NavigationConfig> configure)
    {
        configure(_config.Navigation);
        return this;
    }

    public UiConfigBuilder AddDataBinding(string dataSource, string dataMember, string formatString = "")
    {
        _config.DataBindings.Add(new DataBindingConfig
        {
            DataSource = dataSource,
            DataMember = dataMember,
            FormatString = formatString
        });
        return this;
    }


    public UiConfigBuilder WithStyle(Action<StyleConfig> configure)
    {
        configure(_config.Styles);
        return this;
    }

    public UiConfigBuilder WithDataLoad(Action<DataLoadConfig> configure)
    {
        _config.DataLoad = new DataLoadConfig();
        configure(_config.DataLoad);
        return this;
    }

    public UiConfigBuilder Extends(string parentConfig)
    {
        _config.Extends = parentConfig;
        return this;
    }

    public UIConfiguration Build() => _config;
}
