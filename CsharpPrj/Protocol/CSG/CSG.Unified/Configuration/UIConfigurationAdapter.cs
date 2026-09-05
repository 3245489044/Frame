using CSG.Unified;

namespace CSG.Unified.Configuration;

public class UIConfigurationAdapter : IConfiguration
{
    private readonly object _config;
    private readonly Func<object, string> _getConfigType;
    private readonly Func<object, string> _getSchemaVersion;
    private readonly Func<object, CanonicalForm> _toCanonical;

    public UIConfigurationAdapter(
        object config,
        Func<object, string>? getConfigType = null,
        Func<object, string>? getSchemaVersion = null,
        Func<object, CanonicalForm>? toCanonical = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _getConfigType = getConfigType ?? (_ => "UI");
        _getSchemaVersion = getSchemaVersion ?? (_ => "1.0");
        _toCanonical = toCanonical ?? DefaultToCanonical;
    }

    public string ConfigType => _getConfigType(_config);

    public string SchemaVersion => _getSchemaVersion(_config);

    public CanonicalForm ToCanonical() => _toCanonical(_config);

    private static CanonicalForm DefaultToCanonical(object config)
    {
        var form = new CanonicalForm();
        form.Metadata["source"] = "UIConfiguration";
        form.Metadata["version"] = "1.0";
        form.Metadata["adapter"] = typeof(UIConfigurationAdapter).Name;
        return form;
    }
}
