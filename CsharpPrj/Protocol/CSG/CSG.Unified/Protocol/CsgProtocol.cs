namespace CSG.Unified;

public static class CsgProtocol
{
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public static readonly Version MinimumSupportedVersion = new(1, 0, 0);

    public const string Name = "CSG Unified Protocol";

    public const string Description = "Configuration-StateMachine-Graph 统一中间表示协议";

    public static bool IsCompatible(Version version)
        => version >= MinimumSupportedVersion && version <= CurrentVersion;

    public static void EnsureCompatible(Version version, string source = "")
    {
        if (!IsCompatible(version))
        {
            var prefix = string.IsNullOrEmpty(source) ? "" : $"[{source}] ";
            throw new InvalidOperationException(
                $"{prefix}协议版本不兼容: 当前 {CurrentVersion}, 最低支持 {MinimumSupportedVersion}, 传入 {version}");
        }
    }

    public static string GetInfo()
        => $"{Name} v{CurrentVersion} (min: v{MinimumSupportedVersion}) - {Description}";
}
