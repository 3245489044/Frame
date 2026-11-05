using System.Security.Cryptography;
using System.Text;

namespace Modules.Wiring.Model;

/// <summary>
/// 声明目录（只读投影）：六类声明 + 确定性指纹（同源两次投影指纹相等）。
/// <para>指纹载体 = 规范化文本（逐条按 Id/Name 序），不含时间/路径/执行者叙述。</para>
/// </summary>
public sealed record WiringCatalog(
    string SourceId,
    string Fingerprint,
    IReadOnlyList<FormSpec> Forms,
    IReadOnlyList<BoxSpec> Boxes,
    IReadOnlyList<WireSpec> Wires,
    IReadOnlyList<AdapterSpec> Adapters,
    IReadOnlyList<ProfileSpec> Profiles)
{
    public static WiringCatalog Empty { get; } = new(
        string.Empty, string.Empty,
        Array.Empty<FormSpec>(), Array.Empty<BoxSpec>(), Array.Empty<WireSpec>(),
        Array.Empty<AdapterSpec>(), Array.Empty<ProfileSpec>());

    public bool IsEmpty => Boxes.Count == 0 && Wires.Count == 0;

    /// <summary>扁平端口集（按坐标索引）。</summary>
    public IReadOnlyList<PortSpec> Ports
    {
        get
        {
            var ports = new List<PortSpec>();
            foreach (var box in Boxes) ports.AddRange(box.Ports);
            return ports;
        }
    }

    public PortSpec? FindPort(string reference)
        => TryFindPort(reference, out var port) ? port : null;

    public bool TryFindPort(string reference, out PortSpec port)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            foreach (var box in Boxes)
            {
                foreach (var candidate in box.Ports)
                {
                    if (string.Equals(candidate.Ref, reference, StringComparison.Ordinal))
                    {
                        port = candidate;
                        return true;
                    }
                }
            }
        }

        port = null!;
        return false;
    }

    public FormSpec? FindForm(string formId)
    {
        foreach (var form in Forms)
        {
            if (string.Equals(form.Id, formId, StringComparison.Ordinal)) return form;
        }

        return null;
    }

    public WireSpec? FindWire(string wireId)
    {
        foreach (var wire in Wires)
        {
            if (string.Equals(wire.Id, wireId, StringComparison.Ordinal)) return wire;
        }

        return null;
    }

    public BoxSpec? FindBox(string boxId)
    {
        foreach (var box in Boxes)
        {
            if (string.Equals(box.Id, boxId, StringComparison.Ordinal)) return box;
        }

        return null;
    }

    public AdapterSpec? FindAdapter(string adapterId)
    {
        foreach (var adapter in Adapters)
        {
            if (string.Equals(adapter.Id, adapterId, StringComparison.Ordinal)) return adapter;
        }

        return null;
    }
}
