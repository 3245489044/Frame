using CSG.Unified;

namespace CSG.Unified;

public class DefaultConfiguration : IConfiguration
{
    private CanonicalForm? _form;

    public DefaultConfiguration()
    {
    }

    public DefaultConfiguration(CanonicalForm form)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
    }

    public void SetForm(CanonicalForm form)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
    }

    public CanonicalForm? Form => _form;

    public string ConfigType => "Default";

    public string SchemaVersion => "1.0";

    public Version ProtocolVersion => CsgProtocol.CurrentVersion;

    public CanonicalForm ToCanonical()
    {
        return _form ?? new CanonicalForm
        {
            Metadata = new Dictionary<string, object?>
            {
                ["source"] = "DefaultConfiguration",
                ["version"] = SchemaVersion
            }
        };
    }
}
