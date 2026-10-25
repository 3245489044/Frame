using System.Text.Json;
using Modules.Wiring.Abstractions;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;

namespace Modules.Wiring.Declaration;

/// <summary>声明装载结果（来源 + 装载期结构码；装载失败不产出来源内容）。</summary>
public sealed record WiringDeclarationLoadResult(
    IWiringDeclarationSource Source,
    IReadOnlyList<WiringViolation> Violations)
{
    public bool HasViolations => Violations.Count > 0;
}

/// <summary>
/// JSON 声明载体（BCL <see cref="JsonDocument"/>；**不是** ushar 解析器）。
/// <para>ushar 适配器属上层（复用既有解析链），本类只服务测试、早期接入与跨进程声明交换。</para>
/// </summary>
public static partial class JsonWiringDeclarationSource
{
    public static WiringDeclarationLoadResult Load(string json, string sourceId = "json")
    {
        var violations = new List<WiringViolation>();
        if (string.IsNullOrWhiteSpace(json))
        {
            violations.Add(WiringViolation.Create(WiringReasonCodes.WireDeclInvalid, sourceId, "声明为空"));
            return new(WiringDeclaration.Empty with { SourceId = sourceId }, violations);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                violations.Add(WiringViolation.Create(WiringReasonCodes.WireDeclInvalid, sourceId, "声明根须为对象"));
                return new(WiringDeclaration.Empty with { SourceId = sourceId }, violations);
            }

            var source = new WiringDeclaration(
                sourceId,
                ReadForms(root, sourceId, violations),
                ReadBoxes(root, sourceId, violations),
                ReadWires(root, sourceId, violations),
                ReadAdapters(root, sourceId, violations),
                ReadProfiles(root, sourceId, violations));

            return new(source, violations);
        }
        catch (JsonException exception)
        {
            violations.Add(WiringViolation.Create(
                WiringReasonCodes.WireDeclInvalid, sourceId, "JSON 解析失败: " + exception.Message));
            return new(WiringDeclaration.Empty with { SourceId = sourceId }, violations);
        }
    }

    private static IReadOnlyList<FormSpec> ReadForms(JsonElement root, string sourceId, List<WiringViolation> violations)
    {
        var forms = new List<FormSpec>();
        foreach (var item in Items(root, "forms"))
        {
            var id = Text(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                violations.Add(WiringViolation.Create(WiringReasonCodes.WireDeclInvalid, sourceId, "form 缺 id"));
                continue;
            }

            var clrName = Text(item, "clr");
            var clrType = string.IsNullOrWhiteSpace(clrName) ? null : Type.GetType(clrName, throwOnError: false);
            forms.Add(new FormSpec(id, clrType, Flag(item, "sealed")));
        }

        return forms;
    }

    private static IReadOnlyList<BoxSpec> ReadBoxes(JsonElement root, string sourceId, List<WiringViolation> violations)
    {
        var boxes = new List<BoxSpec>();
        foreach (var item in Items(root, "boxes"))
        {
            var id = Text(item, "id");
            var impl = Text(item, "impl");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(impl))
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireDeclInvalid, id ?? sourceId, "box 缺 id 或 impl"));
                continue;
            }

            var ports = new List<PortSpec>();
            foreach (var port in Items(item, "ports"))
            {
                var name = Text(port, "name");
                var form = Text(port, "form");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(form))
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireDeclInvalid, id, "port 缺 name 或 form"));
                    continue;
                }

                ports.Add(new PortSpec(
                    id, name, ParseDirection(Text(port, "dir")), form,
                    Required: Flag(port, "required"),
                    Card: Integer(port, "card", 1),
                    Scope: Text(port, "scope") ?? "global",
                    KeyTemplate: Text(port, "key")));
            }

            foreach (var token in Strings(item, "in"))
            {
                if (TryParsePortToken(id, token, PortDirection.In, violations, out var port)) ports.Add(port);
            }

            foreach (var token in Strings(item, "out"))
            {
                if (TryParsePortToken(id, token, PortDirection.Out, violations, out var port)) ports.Add(port);
            }

            boxes.Add(new BoxSpec(id, impl, ports, Flag(item, "terminal")));
        }

        return boxes;
    }
}
