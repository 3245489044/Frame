using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Usharp.Compute;

public sealed class ComputeInstruction
{
    public string Op { get; private init; } = "";
    public long Number { get; private init; }
    public int Count { get; private init; }
    public string Text { get; private init; } = "";
    public string Aux { get; private init; } = "";
    public string Rule { get; private init; } = "";
    public int Scale { get; private init; }

    public static ComputeInstruction PushInt(long value) => new() { Op = "push_int", Number = value };
    public static ComputeInstruction PushDecimal(string canonical) => new() { Op = "push_dec", Text = canonical };
    public static ComputeInstruction PushString(string value) => new() { Op = "push_str", Text = value };
    public static ComputeInstruction PushBool(bool value) => new() { Op = "push_bool", Number = value ? 1 : 0 };
    public static ComputeInstruction Load(int slot) => new() { Op = "load", Count = slot };
    public static ComputeInstruction Store(int slot) => new() { Op = "store", Count = slot };
    public static ComputeInstruction Binary(string fn) => new() { Op = "bin", Text = fn };
    public static ComputeInstruction Compare(string fn) => new() { Op = "cmp", Text = fn };
    public static ComputeInstruction Unary(string fn) => new() { Op = "un", Text = fn };
    public static ComputeInstruction Jump(int target) => new() { Op = "jump", Count = target };
    public static ComputeInstruction JumpIfFalse(int target) => new() { Op = "jump_if_false", Count = target };
    public static ComputeInstruction Call(string unit, int argc) => new() { Op = "call", Text = unit, Count = argc };
    public static ComputeInstruction Builtin(string name, int argc) => new() { Op = "builtin", Text = name, Count = argc };
    public static ComputeInstruction Convert(string from, string to, string rule, int scale = 0)
        => new() { Op = "convert", Text = from, Aux = to, Rule = rule, Scale = scale };
    public static ComputeInstruction NewList(int count) => new() { Op = "list", Count = count };
    public static ComputeInstruction NewMap(int count) => new() { Op = "map", Count = count };
    public static ComputeInstruction ReturnValue() => new() { Op = "ret" };
    public static ComputeInstruction ReturnUnit() => new() { Op = "ret_unit" };
}

public sealed record ComputeSourceMapEntry(int Pc, int Line);

public sealed record ComputeLoopMeta(int Line, long? From, long? To, long Step, long? Iterations);

public sealed class ComputeUnitIr
{
    public required string CalcId { get; init; }
    public required ComputeUnitKind Kind { get; init; }
    public required IReadOnlyList<ComputeArgument> Arguments { get; init; }
    public required ComputeType ReturnType { get; init; }
    public required bool HasReturn { get; init; }
    public required ComputePurityPolicy Purity { get; init; }
    public required IReadOnlyList<ComputeEffect> Effects { get; init; }
    public required long? Budget { get; init; }
    public required long Slice { get; init; }
    public required long Heap { get; init; }
    public required long Depth { get; init; }
    public required int LocalCount { get; init; }
    public required string SourceFile { get; init; }
    public required int LineNumber { get; init; }
    public required IReadOnlyList<ComputeInstruction> Code { get; init; }
    public required IReadOnlyList<ComputeSourceMapEntry> SourceMap { get; init; }
    public required IReadOnlyList<ComputeLoopMeta> Loops { get; init; }
}

public sealed class ComputeProgram
{
    public const string IrVersion = "compute-ir/1";
    public const int BytecodeVersion = 1;
    public const int VmVersion = 1;

    public ComputeProgram(string rootId, IReadOnlyList<ComputeUnitIr> units, string hash)
    {
        RootId = rootId;
        Units = units;
        Hash = hash;
    }

    public string RootId { get; }
    public IReadOnlyList<ComputeUnitIr> Units { get; }
    public string Hash { get; }

    public ComputeUnitIr? FindUnit(string calcId)
        => Units.FirstOrDefault(u => string.Equals(u.CalcId, calcId, StringComparison.Ordinal));
}

public sealed record ComputeProgramDecodeResult(
    bool Ok, ComputeProgram? Program, string Code, string Message)
{
    public static ComputeProgramDecodeResult Success(ComputeProgram program)
        => new(true, program, "", "");

    public static ComputeProgramDecodeResult Reject(string message)
        => new(false, null, ComputeCodes.VersionRejected, message);
}

public static class ComputeProgramCodec
{
    public static string ToJson(ComputeProgram program) => Serialize(program, includeHash: true);
    public static string ToCanonicalJson(ComputeProgram program) => Serialize(program, includeHash: false);

    public static string ComputeHash(ComputeProgram program)
        => "sha256:" + HashHex(Serialize(program, includeHash: false));

    private static string Serialize(ComputeProgram program, bool includeHash)
    {
        var root = Cj.Obj()
            .Add("ir", ComputeProgram.IrVersion)
            .Add("bytecodeVersion", ComputeProgram.BytecodeVersion)
            .Add("vmVersion", ComputeProgram.VmVersion)
            .Add("root", program.RootId)
            .Add("units", UnitArray(program.Units));
        if (includeHash) root.Add("hash", program.Hash);
        return CjWriter.Write(root);
    }

    private static CjArray UnitArray(IReadOnlyList<ComputeUnitIr> units)
    {
        var array = Cj.Arr();
        foreach (var unit in units.OrderBy(u => u.CalcId, StringComparer.Ordinal))
        {
            array.Add(UnitObject(unit));
        }

        return array;
    }

    private static CjObject UnitObject(ComputeUnitIr unit)
    {
        var args = Cj.Arr();
        foreach (var argument in unit.Arguments)
        {
            args.Add(Cj.Obj().Add("name", argument.Name).Add("type", argument.Type.Display));
        }

        var effects = Cj.Arr();
        foreach (var effect in unit.Effects) effects.Add(EffectText(effect));

        var code = Cj.Arr();
        foreach (var instruction in unit.Code) code.Add(InstructionObject(instruction));

        var sourceMap = Cj.Arr();
        foreach (var entry in unit.SourceMap)
            sourceMap.Add(Cj.Obj().Add("pc", entry.Pc).Add("line", entry.Line));

        var loops = Cj.Arr();
        foreach (var loop in unit.Loops)
        {
            var item = Cj.Obj().Add("line", loop.Line).Add("step", loop.Step);
            if (loop.From.HasValue) item.Add("from", loop.From.Value);
            if (loop.To.HasValue) item.Add("to", loop.To.Value);
            if (loop.Iterations.HasValue) item.Add("iterations", loop.Iterations.Value);
            loops.Add(item);
        }

        var obj = Cj.Obj()
            .Add("calcId", unit.CalcId)
            .Add("kind", KindText(unit.Kind))
            .Add("args", args)
            .Add("returns", unit.ReturnType.Display)
            .Add("pure", PurityText(unit.Purity))
            .Add("effects", effects)
            .Add("slice", unit.Slice)
            .Add("heap", unit.Heap)
            .Add("depth", unit.Depth)
            .Add("locals", unit.LocalCount)
            .Add("source", Cj.Obj().Add("file", unit.SourceFile).Add("line", unit.LineNumber))
            .Add("code", code)
            .Add("sourceMap", sourceMap)
            .Add("loops", loops);
        if (unit.Budget.HasValue) obj.Add("budget", unit.Budget.Value);
        return obj;
    }

    private static CjObject InstructionObject(ComputeInstruction instruction)
    {
        var obj = Cj.Obj().Add("op", instruction.Op);
        switch (instruction.Op)
        {
            case "push_int":
                obj.Add("value", instruction.Number);
                break;
            case "push_dec":
                obj.Add("value", instruction.Text);
                break;
            case "push_str":
                obj.Add("value", instruction.Text);
                break;
            case "push_bool":
                obj.Add("value", instruction.Number != 0);
                break;
            case "load":
            case "store":
                obj.Add("slot", instruction.Count);
                break;
            case "bin":
            case "cmp":
            case "un":
                obj.Add("fn", instruction.Text);
                break;
            case "jump":
            case "jump_if_false":
                obj.Add("target", instruction.Count);
                break;
            case "call":
                obj.Add("unit", instruction.Text).Add("argc", instruction.Count);
                break;
            case "builtin":
                obj.Add("name", instruction.Text).Add("argc", instruction.Count);
                break;
            case "convert":
                obj.Add("from", instruction.Text).Add("to", instruction.Aux).Add("rule", instruction.Rule);
                if (instruction.Scale > 0) obj.Add("scale", instruction.Scale);
                break;
            case "list":
            case "map":
                obj.Add("count", instruction.Count);
                break;
        }

        return obj;
    }

    public static string KindText(ComputeUnitKind kind) => kind switch
    {
        ComputeUnitKind.Expr => "expr",
        ComputeUnitKind.Fn => "fn",
        _ => "proc"
    };

    public static string PurityText(ComputePurityPolicy purity) => purity switch
    {
        ComputePurityPolicy.Strict => "strict",
        ComputePurityPolicy.Scoped => "scoped",
        _ => "impure"
    };

    public static string EffectText(ComputeEffect effect) => effect switch
    {
        ComputeEffect.KeyRead => "ks.read",
        ComputeEffect.KeyWrite => "ks.write",
        ComputeEffect.Emit => "emit",
        ComputeEffect.Clock => "clock",
        _ => "random"
    };

    public static ComputeProgramDecodeResult TryDecode(string json, string expectedHash = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ComputeProgramDecodeResult.Reject("程序 JSON 不是对象");

            if (!root.TryGetProperty("ir", out var ir) || ir.ValueKind != JsonValueKind.String
                || !string.Equals(ir.GetString(), ComputeProgram.IrVersion, StringComparison.Ordinal))
            {
                return ComputeProgramDecodeResult.Reject(
                    $"程序 IR 版本不兼容：期望 '{ComputeProgram.IrVersion}'");
            }

            if (!root.TryGetProperty("bytecodeVersion", out var bytecode)
                || bytecode.ValueKind != JsonValueKind.Number
                || bytecode.GetInt32() != ComputeProgram.BytecodeVersion)
            {
                return ComputeProgramDecodeResult.Reject(
                    $"字节码版本不兼容：期望 {ComputeProgram.BytecodeVersion}");
            }

            if (!root.TryGetProperty("vmVersion", out var vm)
                || vm.ValueKind != JsonValueKind.Number
                || vm.GetInt32() != ComputeProgram.VmVersion)
            {
                return ComputeProgramDecodeResult.Reject(
                    $"VM 版本不兼容：期望 {ComputeProgram.VmVersion}");
            }

            if (!root.TryGetProperty("root", out var rootId) || rootId.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("units", out var units) || units.ValueKind != JsonValueKind.Array)
            {
                return ComputeProgramDecodeResult.Reject("程序 JSON 缺少 root/units");
            }

            var parsedUnits = new List<ComputeUnitIr>();
            foreach (var unit in units.EnumerateArray())
            {
                parsedUnits.Add(ParseUnit(unit));
            }

            var program = new ComputeProgram(rootId.GetString()!, parsedUnits, "");
            var computed = ComputeHash(program);
            if (expectedHash is not null && !string.Equals(computed, expectedHash, StringComparison.Ordinal))
                return ComputeProgramDecodeResult.Reject("程序哈希与期望不一致");

            if (root.TryGetProperty("hash", out var stored) && stored.ValueKind == JsonValueKind.String
                && !string.Equals(stored.GetString(), computed, StringComparison.Ordinal))
            {
                return ComputeProgramDecodeResult.Reject("程序 JSON 哈希校验失败");
            }

            if (!program.Units.Any(u => string.Equals(u.CalcId, program.RootId, StringComparison.Ordinal)))
                return ComputeProgramDecodeResult.Reject($"根单元 '{program.RootId}' 不在 units 中");

            var result = new ComputeProgram(program.RootId, program.Units, computed);
            return ComputeProgramDecodeResult.Success(result);
        }
        catch (Exception ex)
        {
            return ComputeProgramDecodeResult.Reject($"程序 JSON 解析失败：{ex.Message}");
        }
    }

    private static ComputeUnitIr ParseUnit(JsonElement element)
    {
        var args = new List<ComputeArgument>();
        if (element.TryGetProperty("args", out var argsElement))
        {
            foreach (var argument in argsElement.EnumerateArray())
            {
                if (!ComputeTypeParser.TryParse(argument.GetProperty("type").GetString(), out var type, out var error))
                    throw new InvalidOperationException(error);
                args.Add(new ComputeArgument(argument.GetProperty("name").GetString()!, type));
            }
        }

        var effects = new List<ComputeEffect>();
        if (element.TryGetProperty("effects", out var effectsElement))
        {
            foreach (var effect in effectsElement.EnumerateArray())
                effects.Add(ParseEffect(effect.GetString()!));
        }

        var code = new List<ComputeInstruction>();
        foreach (var instruction in element.GetProperty("code").EnumerateArray())
            code.Add(ParseInstruction(instruction));

        var sourceMap = new List<ComputeSourceMapEntry>();
        foreach (var entry in element.GetProperty("sourceMap").EnumerateArray())
            sourceMap.Add(new ComputeSourceMapEntry(entry.GetProperty("pc").GetInt32(),
                entry.GetProperty("line").GetInt32()));

        var loops = new List<ComputeLoopMeta>();
        foreach (var loop in element.GetProperty("loops").EnumerateArray())
        {
            loops.Add(new ComputeLoopMeta(
                loop.GetProperty("line").GetInt32(),
                loop.TryGetProperty("from", out var from) ? from.GetInt64() : null,
                loop.TryGetProperty("to", out var to) ? to.GetInt64() : null,
                loop.GetProperty("step").GetInt64(),
                loop.TryGetProperty("iterations", out var iterations) ? iterations.GetInt64() : null));
        }

        if (!ComputeTypeParser.TryParse(element.GetProperty("returns").GetString(), out var returnType,
                out var returnError))
            throw new InvalidOperationException(returnError);

        var source = element.GetProperty("source");
        return new ComputeUnitIr
        {
            CalcId = element.GetProperty("calcId").GetString()!,
            Kind = ParseKind(element.GetProperty("kind").GetString()!),
            Arguments = args,
            ReturnType = returnType,
            HasReturn = ParseKind(element.GetProperty("kind").GetString()!) != ComputeUnitKind.Proc,
            Purity = ParsePurity(element.GetProperty("pure").GetString()!),
            Effects = effects,
            Budget = element.TryGetProperty("budget", out var budget) ? budget.GetInt64() : null,
            Slice = element.GetProperty("slice").GetInt64(),
            Heap = element.GetProperty("heap").GetInt64(),
            Depth = element.GetProperty("depth").GetInt64(),
            LocalCount = element.GetProperty("locals").GetInt32(),
            SourceFile = source.GetProperty("file").GetString()!,
            LineNumber = source.GetProperty("line").GetInt32(),
            Code = code,
            SourceMap = sourceMap,
            Loops = loops
        };
    }

    private static ComputeInstruction ParseInstruction(JsonElement element)
    {
        var op = element.GetProperty("op").GetString()!;
        return op switch
        {
            "push_int" => ComputeInstruction.PushInt(element.GetProperty("value").GetInt64()),
            "push_dec" => ComputeInstruction.PushDecimal(element.GetProperty("value").GetString()!),
            "push_str" => ComputeInstruction.PushString(element.GetProperty("value").GetString()!),
            "push_bool" => ComputeInstruction.PushBool(element.GetProperty("value").GetBoolean()),
            "load" => ComputeInstruction.Load(element.GetProperty("slot").GetInt32()),
            "store" => ComputeInstruction.Store(element.GetProperty("slot").GetInt32()),
            "bin" => ComputeInstruction.Binary(element.GetProperty("fn").GetString()!),
            "cmp" => ComputeInstruction.Compare(element.GetProperty("fn").GetString()!),
            "un" => ComputeInstruction.Unary(element.GetProperty("fn").GetString()!),
            "jump" => ComputeInstruction.Jump(element.GetProperty("target").GetInt32()),
            "jump_if_false" => ComputeInstruction.JumpIfFalse(element.GetProperty("target").GetInt32()),
            "call" => ComputeInstruction.Call(element.GetProperty("unit").GetString()!,
                element.GetProperty("argc").GetInt32()),
            "builtin" => ComputeInstruction.Builtin(element.GetProperty("name").GetString()!,
                element.GetProperty("argc").GetInt32()),
            "convert" => ComputeInstruction.Convert(element.GetProperty("from").GetString()!,
                element.GetProperty("to").GetString()!, element.GetProperty("rule").GetString()!,
                element.TryGetProperty("scale", out var scale) ? scale.GetInt32() : 0),
            "list" => ComputeInstruction.NewList(element.GetProperty("count").GetInt32()),
            "map" => ComputeInstruction.NewMap(element.GetProperty("count").GetInt32()),
            "ret" => ComputeInstruction.ReturnValue(),
            "ret_unit" => ComputeInstruction.ReturnUnit(),
            _ => throw new InvalidOperationException($"未知字节码指令 '{op}'")
        };
    }

    private static ComputeUnitKind ParseKind(string text) => text switch
    {
        "expr" => ComputeUnitKind.Expr,
        "fn" => ComputeUnitKind.Fn,
        "proc" => ComputeUnitKind.Proc,
        _ => throw new InvalidOperationException($"未知 kind '{text}'")
    };

    private static ComputePurityPolicy ParsePurity(string text) => text switch
    {
        "strict" => ComputePurityPolicy.Strict,
        "scoped" => ComputePurityPolicy.Scoped,
        "impure" => ComputePurityPolicy.Impure,
        _ => throw new InvalidOperationException($"未知 pure '{text}'")
    };

    private static ComputeEffect ParseEffect(string text) => text switch
    {
        "ks.read" => ComputeEffect.KeyRead,
        "ks.write" => ComputeEffect.KeyWrite,
        "emit" => ComputeEffect.Emit,
        "clock" => ComputeEffect.Clock,
        "random" => ComputeEffect.Random,
        _ => throw new InvalidOperationException($"未知 effect '{text}'")
    };

    private static string HashHex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
