using System.Globalization;
using System.Text.Json;

namespace Usharp.Compute;

/// <summary>
/// 快照编解码。策略固定为「拒绝并标记」：snapshotVersion / vmVersion / bytecodeVersion / programHash
/// 任一不符即拒绝，返回 US8410 并附 MarkCode=US8407（StateDiscarded），绝不静默解码。
/// </summary>
public static class ComputeSnapshotCodec
{
    public static string ToJson(ComputeSnapshot snapshot)
    {
        var frames = Cj.Arr();
        foreach (var frame in snapshot.Frames)
        {
            var locals = Cj.Arr();
            foreach (var local in frame.Locals) locals.Add(ValueNode(local));
            frames.Add(Cj.Obj()
                .Add("unit", frame.Unit)
                .Add("pc", frame.Pc)
                .Add("stackBase", frame.StackBase)
                .Add("returnPc", frame.ReturnPc)
                .Add("steps", frame.Steps)
                .Add("locals", locals));
        }

        var stack = Cj.Arr();
        foreach (var value in snapshot.Stack) stack.Add(ValueNode(value));

        var effects = Cj.Arr();
        foreach (var effect in snapshot.Effects)
        {
            effects.Add(Cj.Obj()
                .Add("kind", effect.Kind)
                .Add("target", effect.Target)
                .Add("value", effect.Value)
                .Add("note", effect.Note));
        }

        return CjWriter.Write(Cj.Obj()
            .Add("snapshotVersion", snapshot.SnapshotVersion)
            .Add("vmVersion", snapshot.VmVersion)
            .Add("bytecodeVersion", snapshot.BytecodeVersion)
            .Add("programHash", snapshot.ProgramHash)
            .Add("status", StatusText(snapshot.Status))
            .Add("steps", snapshot.Steps)
            .Add("frames", frames)
            .Add("stack", stack)
            .Add("effects", effects));
    }

    private static CjNode ValueNode(ComputeValue? value)
    {
        if (value is null) return Cj.Obj().Add("type", "void");
        return value.Kind switch
        {
            ComputeValueKind.Int => Cj.Obj().Add("type", "int").Add("value", value.IntValue),
            ComputeValueKind.Decimal => Cj.Obj().Add("type", "decimal").Add("value", ComputeValue.DecimalText(value.DecimalValue)),
            ComputeValueKind.Bool => Cj.Obj().Add("type", "bool").Add("value", value.BoolValue),
            ComputeValueKind.String => Cj.Obj().Add("type", "string").Add("value", value.Text),
            ComputeValueKind.Unit => Cj.Obj().Add("type", "unit"),
            ComputeValueKind.Host => Cj.Obj().Add("type", "host")
                .Add("host", value.HostValue!.Kind.ToString().ToLowerInvariant())
                .Add("value", value.HostValue!.CanonicalText),
            ComputeValueKind.List => Cj.Obj().Add("type", "list").Add("items", ItemsNode(value.Items)),
            ComputeValueKind.Map => Cj.Obj().Add("type", "map").Add("entries", EntriesNode(value.Entries)),
            _ => Cj.Obj().Add("type", "void")
        };
    }

    private static CjArray ItemsNode(IReadOnlyList<ComputeValue> items)
    {
        var array = Cj.Arr();
        foreach (var item in items) array.Add(ValueNode(item));
        return array;
    }

    private static CjArray EntriesNode(IReadOnlyList<KeyValuePair<string, ComputeValue>> entries)
    {
        var array = Cj.Arr();
        foreach (var entry in entries)
            array.Add(Cj.Obj().Add("key", entry.Key).Add("value", ValueNode(entry.Value)));
        return array;
    }

    public static ComputeSnapshotLoadResult TryDecode(string json, ComputeProgram program)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Reject("快照 JSON 不是对象");

            var snapshotVersion = root.GetProperty("snapshotVersion").GetInt32();
            if (snapshotVersion != ComputeSnapshot.LatestVersion)
                return Reject($"快照版本 {snapshotVersion} 与当前 {ComputeSnapshot.LatestVersion} 不兼容");

            var vmVersion = root.GetProperty("vmVersion").GetInt32();
            if (vmVersion != ComputeProgram.VmVersion)
                return Reject($"快照 VM 版本 {vmVersion} 与当前 {ComputeProgram.VmVersion} 不兼容");

            var bytecodeVersion = root.GetProperty("bytecodeVersion").GetInt32();
            if (bytecodeVersion != ComputeProgram.BytecodeVersion)
                return Reject($"快照字节码版本 {bytecodeVersion} 与当前 {ComputeProgram.BytecodeVersion} 不兼容");

            var programHash = root.GetProperty("programHash").GetString() ?? "";
            if (!string.Equals(programHash, program.Hash, StringComparison.Ordinal))
                return Reject("快照 programHash 与程序不一致");

            var frames = new List<ComputeSnapshotFrame>();
            foreach (var frame in root.GetProperty("frames").EnumerateArray())
            {
                var locals = new List<ComputeValue?>();
                foreach (var local in frame.GetProperty("locals").EnumerateArray())
                    locals.Add(ParseValue(local));
                frames.Add(new ComputeSnapshotFrame(
                    frame.GetProperty("unit").GetString()!,
                    frame.GetProperty("pc").GetInt32(),
                    frame.GetProperty("stackBase").GetInt32(),
                    frame.GetProperty("returnPc").GetInt32(),
                    frame.GetProperty("steps").GetInt64(),
                    locals));
            }

            var stack = new List<ComputeValue>();
            foreach (var value in root.GetProperty("stack").EnumerateArray())
            {
                var parsed = ParseValue(value);
                if (parsed is null) throw new InvalidOperationException("操作数栈不能为空值");
                stack.Add(parsed);
            }

            var effects = new List<ComputeEffectRecord>();
            foreach (var effect in root.GetProperty("effects").EnumerateArray())
            {
                effects.Add(new ComputeEffectRecord(
                    effect.GetProperty("kind").GetString()!,
                    effect.GetProperty("target").GetString()!,
                    effect.GetProperty("value").GetString()!,
                    effect.GetProperty("note").GetString()!));
            }

            var snapshot = new ComputeSnapshot(snapshotVersion, vmVersion, bytecodeVersion, programHash,
                ParseStatus(root.GetProperty("status").GetString()!), root.GetProperty("steps").GetInt64(),
                frames, stack, effects);
            return new ComputeSnapshotLoadResult(true, snapshot, "", "", null);
        }
        catch (Exception ex)
        {
            return Reject($"快照 JSON 解析失败：{ex.Message}");
        }
    }

    private static ComputeSnapshotLoadResult Reject(string message)
        => new(false, null, ComputeCodes.VersionRejected, message, ComputeCodes.StateDiscarded);

    private static ComputeValue? ParseValue(JsonElement element)
    {
        var type = element.GetProperty("type").GetString();
        switch (type)
        {
            case "void":
                return null;
            case "int":
                return ComputeValue.Int(element.GetProperty("value").GetInt64());
            case "decimal":
                return ComputeValue.Decimal(decimal.Parse(element.GetProperty("value").GetString()!,
                    CultureInfo.InvariantCulture));
            case "bool":
                return ComputeValue.Bool(element.GetProperty("value").GetBoolean());
            case "string":
                return ComputeValue.String(element.GetProperty("value").GetString()!);
            case "unit":
                return ComputeValue.UnitValue;
            case "host":
                return ComputeValue.Host(ParseHost(element));
            case "list":
            {
                var items = new List<ComputeValue>();
                foreach (var item in element.GetProperty("items").EnumerateArray())
                {
                    var parsed = ParseValue(item);
                    if (parsed is null) throw new InvalidOperationException("列表元素不能为空值");
                    items.Add(parsed);
                }

                return ComputeValue.List(items);
            }
            case "map":
            {
                var entries = new List<KeyValuePair<string, ComputeValue>>();
                foreach (var entry in element.GetProperty("entries").EnumerateArray())
                {
                    var parsed = ParseValue(entry.GetProperty("value"));
                    if (parsed is null) throw new InvalidOperationException("map 值不能为空值");
                    entries.Add(new KeyValuePair<string, ComputeValue>(
                        entry.GetProperty("key").GetString()!, parsed));
                }

                return ComputeValue.Map(entries);
            }
            default:
                throw new InvalidOperationException($"未知值类型 '{type}'");
        }
    }

    private static ComputeHostValue ParseHost(JsonElement element)
    {
        var kind = element.GetProperty("host").GetString()!;
        var text = element.GetProperty("value").GetString()!;
        return kind switch
        {
            "int" => ComputeHostValue.FromInt(long.Parse(text, CultureInfo.InvariantCulture)),
            "decimal" => new ComputeHostValue(ComputeHostValueKind.Decimal, text),
            "bool" => ComputeHostValue.FromBool(string.Equals(text, "true", StringComparison.Ordinal)),
            "string" => ComputeHostValue.FromString(text),
            _ => throw new InvalidOperationException($"未知 host 值类型 '{kind}'")
        };
    }

    private static string StatusText(ComputeStatus status) => status switch
    {
        ComputeStatus.Ready => "ready",
        ComputeStatus.Suspended => "suspended",
        ComputeStatus.Completed => "completed",
        ComputeStatus.BudgetExceeded => "budget-exceeded",
        ComputeStatus.StackResourceExceeded => "stack-resource-exceeded",
        ComputeStatus.HeapResourceExceeded => "heap-resource-exceeded",
        ComputeStatus.EffectDenied => "effect-denied",
        ComputeStatus.Cancelled => "cancelled",
        ComputeStatus.ArithmeticError => "arithmetic-error",
        ComputeStatus.PortFailure => "port-failure",
        _ => "state-discarded"
    };

    private static ComputeStatus ParseStatus(string text) => text switch
    {
        "ready" => ComputeStatus.Ready,
        "suspended" => ComputeStatus.Suspended,
        "completed" => ComputeStatus.Completed,
        "budget-exceeded" => ComputeStatus.BudgetExceeded,
        "stack-resource-exceeded" => ComputeStatus.StackResourceExceeded,
        "heap-resource-exceeded" => ComputeStatus.HeapResourceExceeded,
        "effect-denied" => ComputeStatus.EffectDenied,
        "cancelled" => ComputeStatus.Cancelled,
        "arithmetic-error" => ComputeStatus.ArithmeticError,
        "port-failure" => ComputeStatus.PortFailure,
        _ => ComputeStatus.StateDiscarded
    };
}
