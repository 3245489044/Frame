using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Modules.Wiring.Abstractions;

namespace Modules.Wiring.Model;

/// <summary>
/// 声明 → 目录的**唯一**投影点，以及规范化指纹计算（同源两次投影指纹相等）。
/// </summary>
public static class WiringCatalogFactory
{
    /// <summary>从声明来源投影（排序固定，与插入顺序无关）。</summary>
    public static WiringCatalog FromSource(IWiringDeclarationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var forms = (source.Forms ?? Array.Empty<FormSpec>())
            .OrderBy(form => form.Id, StringComparer.Ordinal)
            .ToArray();
        var boxes = (source.Boxes ?? Array.Empty<BoxSpec>())
            .Select(box => box with
            {
                Ports = (box.Ports ?? Array.Empty<PortSpec>())
                    .OrderBy(port => port.Name, StringComparer.Ordinal)
                    .ToArray(),
            })
            .OrderBy(box => box.Id, StringComparer.Ordinal)
            .ToArray();
        var wires = (source.Wires ?? Array.Empty<WireSpec>())
            .OrderBy(wire => wire.Id, StringComparer.Ordinal)
            .ToArray();
        var adapters = (source.Adapters ?? Array.Empty<AdapterSpec>())
            .OrderBy(adapter => adapter.Id, StringComparer.Ordinal)
            .ToArray();
        var profiles = (source.Profiles ?? Array.Empty<ProfileSpec>())
            .OrderBy(profile => profile.Id, StringComparer.Ordinal)
            .ToArray();

        var fingerprint = ComputeFingerprint(source.SourceId, forms, boxes, wires, adapters, profiles);
        return new WiringCatalog(source.SourceId, fingerprint, forms, boxes, wires, adapters, profiles);
    }

    /// <summary>规范化指纹（结构事实；与对象引用 / 插入顺序无关）。</summary>
    public static string ComputeFingerprint(
        string sourceId,
        IReadOnlyList<FormSpec> forms,
        IReadOnlyList<BoxSpec> boxes,
        IReadOnlyList<WireSpec> wires,
        IReadOnlyList<AdapterSpec> adapters,
        IReadOnlyList<ProfileSpec> profiles)
    {
        var builder = new StringBuilder();
        builder.Append("src=").Append(sourceId ?? string.Empty).Append('\n');

        foreach (var form in forms)
        {
            builder.Append("form|").Append(form.Id).Append('|')
                .Append(form.ClrType?.FullName ?? "-").Append('|')
                .Append(form.Sealed ? "sealed" : "open").Append('\n');
        }

        foreach (var box in boxes)
        {
            builder.Append("box|").Append(box.Id).Append('|').Append(box.Impl).Append('|')
                .Append(box.Terminal ? "terminal" : "flow").Append('\n');
            foreach (var port in box.Ports)
            {
                builder.Append("port|").Append(port.Ref).Append('|')
                    .Append(port.IsIn ? "in" : "out").Append('|')
                    .Append(port.Form).Append('|')
                    .Append(port.Required ? "required" : "optional").Append('|')
                    .Append(port.Card.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(port.Scope).Append('\n');
            }
        }

        foreach (var wire in wires)
        {
            builder.Append("wire|").Append(wire.Id).Append('|').Append(wire.From).Append('|')
                .Append(wire.To).Append('|').Append(wire.Adapter ?? "-").Append('|')
                .Append(wire.Judge ?? "-").Append('|').Append(wire.OnMissing.ToString()).Append('|')
                .Append(wire.Merge.ToString()).Append('|').Append(wire.Each ?? "-").Append('|')
                .Append(wire.Bridge ? "bridge" : "direct").Append('\n');
        }

        foreach (var adapter in adapters)
        {
            builder.Append("adapter|").Append(adapter.Id).Append('|').Append(adapter.From).Append('|')
                .Append(adapter.To).Append('|').Append(adapter.Impl ?? "-").Append('\n');
        }

        foreach (var profile in profiles)
        {
            builder.Append("profile|").Append(profile.Id).Append('|').Append(profile.Scope).Append('|')
                .Append(string.Join(",", profile.Enable.OrderBy(id => id, StringComparer.Ordinal)))
                .Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
