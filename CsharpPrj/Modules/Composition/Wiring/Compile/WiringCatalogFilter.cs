using Modules.Wiring.Model;

namespace Modules.Wiring.Compile;

/// <summary>剖面激活：只保留 profile.enable 内的 wire（指纹按子集重算，可复算）。</summary>
public static class WiringCatalogFilter
{
    /// <summary>
    /// 剖面激活：只保留 profile.enable 内的 wire（指纹按子集重算，可复算）。
    /// <para>未知 profile 不静默返回全量：<paramref name="error"/> 非空即失败可见（WIRE_PROFILE_UNKNOWN）。</para>
    /// </summary>
    public static bool TryApplyProfile(
        WiringCatalog catalog, string? profileId, out WiringCatalog filtered, out string? error)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        filtered = catalog;
        error = null;
        if (string.IsNullOrWhiteSpace(profileId)) return true;

        var profile = catalog.Profiles.FirstOrDefault(
            item => string.Equals(item.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
        {
            error = Diagnostics.WiringReasonCodes.WireProfileUnknown + ":" + profileId;
            return false;
        }

        var enabled = new HashSet<string>(profile.Enable ?? Array.Empty<string>(), StringComparer.Ordinal);
        var wires = catalog.Wires.Where(wire => enabled.Contains(wire.Id)).ToArray();
        var fingerprint = WiringCatalogFactory.ComputeFingerprint(
            catalog.SourceId, catalog.Forms, catalog.Boxes, wires, catalog.Adapters, catalog.Profiles);
        filtered = catalog with { Fingerprint = fingerprint, Wires = wires };
        return true;
    }

    public static WiringCatalog ApplyProfile(WiringCatalog catalog, string? profileId)
        => TryApplyProfile(catalog, profileId, out var filtered, out var error)
            ? filtered
            : throw new ArgumentException("接线剖面未知（" + error + "）", nameof(profileId));
}
