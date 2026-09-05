namespace CSG.Unified;

public interface IConfiguration
{
    string ConfigType { get; }

    string SchemaVersion { get; }

    Version ProtocolVersion => CsgProtocol.CurrentVersion;

    CanonicalForm ToCanonical();
}
