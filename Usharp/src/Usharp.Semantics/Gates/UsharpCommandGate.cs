using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>闸A：<c>[Command]</c> 结构闭包（channel/op/key/chain/port 引用可解析）。</summary>
internal sealed class CommandGateRule : IGateRule<UsharpGateModel>
{
    public string Name => "usharp.command";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        var families = model.KeySpaces.SelectMany(k => k.Families)
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var seenActions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var command in model.Commands)
        {
            foreach (var line in command.UnparsedLines)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandLineUnparsed,
                    $"[Command] {command.Action}: 无法解析的续行 '{line}'", command.SourceFile, command.LineNumber));

            if (!seenActions.Add(command.Action))
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandDuplicate,
                    $"[Command] {command.Action}: 重复声明", command.SourceFile, command.LineNumber));

            if (command.Channel is { } channel)
            {
                if (!channel.Contains('.'))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandChannelMalformed,
                        $"[Command] {command.Action}: channel='{channel}' 不含 '.'（须形如 <space>.<key>）",
                        command.SourceFile, command.LineNumber));
                else
                {
                    var dot = channel.LastIndexOf('.');
                    if (!model.Spaces.HasCoordinate(channel[..dot], channel[(dot + 1)..], out _))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandChannelUnknown,
                            $"[Command] {command.Action}: channel '{channel}' 未声明",
                            command.SourceFile, command.LineNumber));
                }
            }

            var opNames = new HashSet<string>(command.Ops.Select(o => o.Op), StringComparer.Ordinal);
            if (opNames.Contains("*") && opNames.Count > 1)
                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandOpIncomplete,
                    $"[Command] {command.Action}: 'op *' 不得与具名 op 并存",
                    command.SourceFile, command.LineNumber));

            foreach (var op in command.Ops)
            {
                if (!string.Equals(op.Op, "*", StringComparison.Ordinal)
                    && (string.IsNullOrWhiteSpace(op.Handler) || string.IsNullOrWhiteSpace(op.Projector)))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandOpIncomplete,
                        $"[Command] {command.Action}.{op.Op}: 缺 handler=/projector=",
                        command.SourceFile, command.LineNumber));

                foreach (var port in op.Ports) CheckPort(model, port, command, sink);
            }

            foreach (var port in command.Ports) CheckPort(model, port, command, sink);

            foreach (var key in command.Keys)
            {
                if (!opNames.Contains(key.Op))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandOpRefUnknown,
                        $"[Command] {command.Action}: key op='{key.Op}' 未声明",
                        command.SourceFile, command.LineNumber));

                if (!families.TryGetValue(key.Family, out var family))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandKeyFamilyUnknown,
                        $"[Command] {command.Action}: key '{key.Name}' 族 '{key.Family}' 未声明",
                        command.SourceFile, command.LineNumber));
                else if (family.Surfaces.Count > 0
                         && !family.Surfaces.Any(s => string.Equals(s.Surface, key.Surface, StringComparison.OrdinalIgnoreCase)))
                    sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandKeySurfaceUnknown,
                        $"[Command] {command.Action}: key '{key.Name}' 面 '{key.Surface}' 未声明",
                        command.SourceFile, command.LineNumber));

                if (key.Chain.Count > 0)
                {
                    if (!model.Spaces.TryGet("RequestField", out _))
                        sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandSpaceUndeclared,
                            $"[Command] {command.Action}: key '{key.Name}' 声明 chain= 但空间 'RequestField' 未声明（禁止跳过校验）",
                            command.SourceFile, command.LineNumber));
                    else
                        foreach (var field in key.Chain)
                            if (!model.Spaces.HasCoordinate("RequestField", field, out _))
                                sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandKeyChainFieldUnknown,
                                    $"[Command] {command.Action}: chain 字段 '{field}' 未声明",
                                    command.SourceFile, command.LineNumber));
                }
            }

            CheckOpRef(model, command.DefaultOp, "default", opNames, command, sink);
            CheckOpRef(model, command.FallbackOp, "fallback", opNames, command, sink);
        }
    }

    private static void CheckOpRef(UsharpGateModel model, string? op, string label,
        HashSet<string> opNames, UsharpCommandDecl command, IConversionDiagnosticSink sink)
    {
        if (op is { } value && !opNames.Contains(value))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandDefaultUnknown,
                $"[Command] {command.Action}: {label}='{value}' 未声明",
                command.SourceFile, command.LineNumber));
    }

    private static void CheckPort(UsharpGateModel model, string port, UsharpCommandDecl command, IConversionDiagnosticSink sink)
    {
        if (string.IsNullOrWhiteSpace(port)) return;
        if (!model.Spaces.TryGet("CommandPort", out _))
        {
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandSpaceUndeclared,
                $"[Command] {command.Action}: 声明 port '{port}' 但空间 'CommandPort' 未声明（禁止跳过校验）",
                command.SourceFile, command.LineNumber));
            return;
        }

        var coord = UsharpLineReader.Tail(port);
        if (!model.Spaces.HasCoordinate("CommandPort", coord, out _))
            sink.Report(UsharpGateDiagnostics.Error(UsharpCodes.CommandPortUnknown,
                $"[Command] {command.Action}: port '{port}' 未声明",
                command.SourceFile, command.LineNumber));
    }
}
