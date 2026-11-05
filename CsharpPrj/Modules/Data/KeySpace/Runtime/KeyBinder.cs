using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;

namespace Modules.KeySpace.Runtime;

/// <summary>
/// 端口绑定唯一实现（读写同源）：Required 缺失 ⇒ 诊断 + RequiredMissing=true；Optional 缺失 ⇒ Unknown。
/// </summary>
public sealed class KeyBinder : IKeyBinder
{
    private readonly IKeyDiagnosticsSink _diagnostics;

    public KeyBinder(IKeyDiagnosticsSink? diagnostics = null)
        => _diagnostics = diagnostics ?? NullKeyDiagnosticsSink.Instance;

    public KeyBindingResult BindIn(IReadOnlyList<PortContract> ports, IKeyReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var values = new Dictionary<string, KeyValue<object?>>(StringComparer.Ordinal);
        var diagnostics = new List<KeyDiagnostic>();
        var requiredMissing = false;

        foreach (var port in PortContractRules.OfDirection(ports, PortDirection.In))
        {
            var key = PortContractRules.KeyOf(port);
            var value = reader.Read(key);

            if (value.State != KeyValueState.Known)
            {
                if (!string.IsNullOrEmpty(port.DefaultValue))
                {
                    value = KeyValue<object?>.Known(port.DefaultValue);
                }
                else
                {
                    var code = port.Required ? KeyReasonCodes.RequiredMissing : KeyReasonCodes.DeclaredMissing;
                    value = port.Required
                        ? new KeyValue<object?>(KeyValueState.Unknown, null,
                            KeyDiagnostic.Error(code, KeyResolution.Missing, key,
                                $"Required 端口 '{port.Name}' 无值"))
                        : KeyValue<object?>.Unknown(code, $"Optional 端口 '{port.Name}' 无值", key);
                    diagnostics.Add(value.Diagnostic!);
                    requiredMissing |= port.Required;
                }
            }

            values[port.Name] = value;
            if (value.Diagnostic is not null && !diagnostics.Contains(value.Diagnostic))
            {
                diagnostics.Add(value.Diagnostic);
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new KeyBindingResult(values, diagnostics, requiredMissing);
    }

    public KeyBindingResult BindOut(IReadOnlyList<PortContract> ports, IReadOnlyDictionary<string, object?> values)
    {
        values ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        var bound = new Dictionary<string, KeyValue<object?>>(StringComparer.Ordinal);
        var diagnostics = new List<KeyDiagnostic>();
        var requiredMissing = false;

        foreach (var port in PortContractRules.OfDirection(ports, PortDirection.Out))
        {
            var key = PortContractRules.KeyOf(port);
            if (values.TryGetValue(port.Name, out var value) && value is not null)
            {
                bound[port.Name] = KeyValue<object?>.Known(value);
                continue;
            }

            var code = port.Required ? KeyReasonCodes.RequiredMissing : KeyReasonCodes.DeclaredMissing;
            var diagnostic = port.Required
                ? KeyDiagnostic.Error(code, KeyResolution.Missing, key, $"Required 输出端口 '{port.Name}' 未产出")
                : KeyDiagnostic.Warn(code, KeyResolution.Missing, key, $"Optional 输出端口 '{port.Name}' 未产出");
            diagnostics.Add(diagnostic);
            requiredMissing |= port.Required;
            bound[port.Name] = new KeyValue<object?>(KeyValueState.Unknown, null, diagnostic);
        }

        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new KeyBindingResult(bound, diagnostics, requiredMissing);
    }
}
