using Modules.Wiring.Abstractions;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;

namespace Modules.Wiring.Compile;

/// <summary>
/// Verify：D1–D8 全量判据（判据载体 = 端口集合 / 形态 / 坐标 / 规模，结构事实）。
/// <para>D1/D2 的解析半边由 <see cref="WiringLinker"/> 承担；本类只裁决，不解析。</para>
/// </summary>
public static partial class WiringValidator
{
    /// <summary>全量校验（声明级 + D1–D8）。</summary>
    public static WiringReport Verify(
        WiringCatalog catalog,
        IWiringImplementationCatalog? implementations = null,
        IFormResolver? formResolver = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var violations = new List<WiringViolation>();
        violations.AddRange(VerifyDeclaration(catalog));

        var link = WiringLinker.Link(catalog);
        violations.AddRange(link.Violations);

        foreach (var wire in link.Wires)
        {
            // D2：形态闭合（相等，或经已声明 Adapter 且方向一致）
            if (!string.Equals(wire.From.Form, wire.To.Form, StringComparison.Ordinal))
            {
                if (wire.Adapter is null)
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireFormMismatch, wire.Id,
                        "两端形态不等且无 Adapter",
                        "from=" + wire.From.Ref + ":" + wire.From.Form,
                        "to=" + wire.To.Ref + ":" + wire.To.Form));
                }
                else if (!string.Equals(wire.Adapter.From, wire.From.Form, StringComparison.Ordinal) ||
                         !string.Equals(wire.Adapter.To, wire.To.Form, StringComparison.Ordinal))
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireFormMismatch, wire.Id,
                        "Adapter 声明与两端形态不一致",
                        "adapter=" + wire.Adapter.Id + ":" + wire.Adapter.From + "->" + wire.Adapter.To,
                        "from=" + wire.From.Form,
                        "to=" + wire.To.Form));
                }
            }

            // D8：跨 scope 须显式 bridge
            if (!string.Equals(wire.From.Scope, wire.To.Scope, StringComparison.Ordinal) && !wire.Spec.Bridge)
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireScopeEscape, wire.Id,
                    "跨 scope 且未声明 bridge",
                    "from.scope=" + wire.From.Scope,
                    "to.scope=" + wire.To.Scope));
            }
        }

        violations.AddRange(VerifySupply(catalog, link));
        violations.AddRange(VerifyWriters(link));
        violations.AddRange(VerifyAcyclic(link));
        violations.AddRange(VerifyOrphanOut(catalog, link));
        violations.AddRange(VerifyPortDrift(catalog, implementations));

        return WiringReport.Of(violations);
    }

    /// <summary>声明级结构校验（无 D1–D8 语义）：重复 Id / 未注册形态 / 形态未定型 / 剖面引用未知 wire。</summary>
    public static IReadOnlyList<WiringViolation> VerifyDeclaration(WiringCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var violations = new List<WiringViolation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        void Unique(string id, string kind)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                violations.Add(WiringViolation.Create(WiringReasonCodes.WireDeclInvalid, kind, "空 Id"));
                return;
            }

            if (!ids.Add(id))
            {
                violations.Add(WiringViolation.Create(WiringReasonCodes.WireDuplicateId, id, "Id 重复（kind=" + kind + "）"));
            }
        }

        foreach (var form in catalog.Forms) Unique(form.Id, "form");
        foreach (var box in catalog.Boxes) Unique(box.Id, "box");
        foreach (var wire in catalog.Wires) Unique(wire.Id, "wire");
        foreach (var adapter in catalog.Adapters) Unique(adapter.Id, "adapter");
        foreach (var profile in catalog.Profiles) Unique(profile.Id, "profile");

        foreach (var form in catalog.Forms)
        {
            if (form.ClrType is null)
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireFormUnsealed, form.Id, "形态未定型（未声明 clr）"));
            }
        }

        foreach (var box in catalog.Boxes)
        {
            foreach (var port in box.Ports)
            {
                if (catalog.FindForm(port.Form) is null)
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireFormMismatch, port.Ref,
                        "端口引用了未注册形态", "form=" + port.Form, "box=" + box.Id));
                }

                if (!string.Equals(port.Owner, box.Id, StringComparison.Ordinal))
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireDeclInvalid, port.Ref, "端口 Owner 与所在盒不一致",
                        "box=" + box.Id));
                }
            }
        }

        var wireIds = new HashSet<string>(catalog.Wires.Select(wire => wire.Id), StringComparer.Ordinal);
        foreach (var profile in catalog.Profiles)
        {
            foreach (var enabled in profile.Enable)
            {
                if (!wireIds.Contains(enabled))
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireProfileUnknown, profile.Id,
                        "剖面启用了未知 wire", "enable=" + enabled));
                }
            }
        }

        return violations;
    }
}
