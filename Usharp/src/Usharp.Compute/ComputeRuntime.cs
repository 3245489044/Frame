using System.Globalization;

namespace Usharp.Compute;

public enum ComputeValueKind
{
    Int,
    Decimal,
    Bool,
    String,
    Unit,
    Host,
    List,
    Map
}

public sealed class ComputeValue
{
    private ComputeValue(ComputeValueKind kind) => Kind = kind;

    public ComputeValueKind Kind { get; }
    public long IntValue { get; private init; }
    public decimal DecimalValue { get; private init; }
    public bool BoolValue { get; private init; }
    public string Text { get; private init; } = "";
    public IReadOnlyList<ComputeValue> Items { get; private init; } = Array.Empty<ComputeValue>();
    public IReadOnlyList<KeyValuePair<string, ComputeValue>> Entries { get; private init; } =
        Array.Empty<KeyValuePair<string, ComputeValue>>();
    public ComputeHostValue? HostValue { get; private init; }

    public static readonly ComputeValue UnitValue = new(ComputeValueKind.Unit);

    public static ComputeValue Int(long value) => new(ComputeValueKind.Int) { IntValue = value };
    public static ComputeValue Decimal(decimal value) => new(ComputeValueKind.Decimal) { DecimalValue = value };
    public static ComputeValue Bool(bool value) => new(ComputeValueKind.Bool) { BoolValue = value };
    public static ComputeValue String(string value) => new(ComputeValueKind.String) { Text = value };

    public static ComputeValue Host(ComputeHostValue value)
        => new(ComputeValueKind.Host) { HostValue = value };

    public static ComputeValue List(IReadOnlyList<ComputeValue> items)
        => new(ComputeValueKind.List) { Items = items };

    public static ComputeValue Map(IReadOnlyList<KeyValuePair<string, ComputeValue>> entries)
        => new(ComputeValueKind.Map) { Entries = entries };

    public string CanonicalText() => Kind switch
    {
        ComputeValueKind.Int => IntValue.ToString(CultureInfo.InvariantCulture),
        ComputeValueKind.Decimal => DecimalText(DecimalValue),
        ComputeValueKind.Bool => BoolValue ? "true" : "false",
        ComputeValueKind.String => Text,
        ComputeValueKind.Unit => "unit",
        ComputeValueKind.Host => HostValue?.CanonicalText ?? "",
        ComputeValueKind.List => "[" + string.Join(",", Items.Select(i => i.CanonicalText())) + "]",
        ComputeValueKind.Map => "{" + string.Join(",", Entries.Select(e => e.Key + ":" + e.Value.CanonicalText())) + "}",
        _ => ""
    };

    public int SizeUnits() => 1 + Kind switch
    {
        ComputeValueKind.String => Text.Length,
        ComputeValueKind.List => Items.Sum(i => i.SizeUnits()),
        ComputeValueKind.Map => Entries.Sum(e => 1 + e.Value.SizeUnits()),
        _ => 0
    };

    public static string DecimalText(decimal value)
        => value.ToString("0.000000", CultureInfo.InvariantCulture);
}

public enum ComputeHostValueKind
{
    Int,
    Decimal,
    String,
    Bool
}

public sealed record ComputeHostValue(ComputeHostValueKind Kind, string CanonicalText)
{
    public static ComputeHostValue FromInt(long value)
        => new(ComputeHostValueKind.Int, value.ToString(CultureInfo.InvariantCulture));

    public static ComputeHostValue FromDecimal(decimal value)
        => new(ComputeHostValueKind.Decimal, ComputeValue.DecimalText(value));

    public static ComputeHostValue FromString(string value)
        => new(ComputeHostValueKind.String, value);

    public static ComputeHostValue FromBool(bool value)
        => new(ComputeHostValueKind.Bool, value ? "true" : "false");
}

public sealed record ComputeConversionTrace(string From, string To, int Scale, string Rounding, string Note);

/// <summary>
/// 宿主浮点边界（需求 2）：double 只能经这里显式进入计算层（scale=6，half-even），并返回留痕记录；
/// 反方向同理。语言内没有 double/float 类型，任何隐式混算都在静态类型检查期被拒（US8005）。
/// </summary>
public static class ComputeHostBoundary
{
    public const int DecimalScale = 6;
    public const string RoundingRule = "half_even";

    public static bool TryFromMatrixDouble(double value, out decimal result,
        out ComputeConversionTrace trace)
    {
        trace = new ComputeConversionTrace("matrix.double", "decimal", DecimalScale, RoundingRule,
            "from_matrix");
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            result = 0m;
            return false;
        }

        try
        {
            result = Quantize((decimal)value);
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    public static ComputeConversionTrace ToMatrixDouble(decimal value, out double result)
    {
        var quantized = Quantize(value);
        result = (double)quantized;
        return new ComputeConversionTrace("decimal", "matrix.double", DecimalScale, RoundingRule,
            "to_matrix");
    }

    public static decimal Quantize(decimal value)
        => Math.Round(value, DecimalScale, MidpointRounding.ToEven);
}

public interface IComputeKeySpacePort
{
    ComputeHostValue? Read(string spaceId, string coordinate);
    void Write(string spaceId, string coordinate, ComputeHostValue value);
}

public interface IComputeCapabilityPort
{
    long ClockNowUnixMs();
    long NextRandomLong(long minInclusive, long maxExclusive);
}

public interface IComputeEmitPort
{
    void Emit(string channel, ComputeHostValue value);
}

public interface IComputeObserver
{
    void OnInstruction(string unitId, int pc, long steps);
}

public interface IComputeHost
{
    IComputeKeySpacePort? KeySpace { get; }
    IComputeCapabilityPort? Capability { get; }
    IComputeEmitPort? Emit { get; }
    IComputeObserver? Observer { get; }
}

public sealed class ComputeHost : IComputeHost
{
    public IComputeKeySpacePort? KeySpace { get; init; }
    public IComputeCapabilityPort? Capability { get; init; }
    public IComputeEmitPort? Emit { get; init; }
    public IComputeObserver? Observer { get; init; }

    public static readonly ComputeHost Empty = new();
}

public enum ComputeStatus
{
    Ready,
    Suspended,
    Completed,
    BudgetExceeded,
    StackResourceExceeded,
    HeapResourceExceeded,
    EffectDenied,
    Cancelled,
    ArithmeticError,
    PortFailure,
    StateDiscarded
}

public sealed record ComputeEffectRecord(string Kind, string Target, string Value, string Note);

public sealed record ComputeResult(ComputeStatus Status, ComputeValue? Output, string Code,
    string Message, long Steps);

internal sealed class ComputeFrameState
{
    public required ComputeUnitIr Unit { get; init; }
    public int Pc { get; set; }
    public int StackBase { get; init; }
    public int ReturnPc { get; init; }
    public long Steps { get; set; }
    public required ComputeValue?[] Locals { get; init; }
}

public sealed class ComputeVm
{
    private readonly ComputeProgram _program;
    private readonly IComputeHost _host;
    private readonly Dictionary<string, ComputeUnitIr> _units = new(StringComparer.Ordinal);
    private readonly List<ComputeFrameState> _frames = new();
    private readonly List<ComputeValue> _stack = new();
    private readonly List<ComputeEffectRecord> _effects = new();

    private long _steps;
    private bool _cancelled;
    private ComputeStatus _status = ComputeStatus.Ready;
    private string _code = "";
    private string _message = "";
    private ComputeValue? _output;

    public ComputeVm(ComputeProgram program, IComputeHost? host = null,
        IReadOnlyList<ComputeValue>? arguments = null)
    {
        _program = program;
        _host = host ?? ComputeHost.Empty;
        foreach (var unit in program.Units) _units[unit.CalcId] = unit;

        var root = _units.GetValueOrDefault(program.RootId)
            ?? throw new ArgumentException($"根单元 '{program.RootId}' 不存在");
        var args = arguments ?? Array.Empty<ComputeValue>();
        if (args.Count != root.Arguments.Count)
            throw new ArgumentException($"根单元 '{root.CalcId}' 需要 {root.Arguments.Count} 个参数，实际 {args.Count}");

        var locals = new ComputeValue?[root.Arguments.Count + root.LocalCount];
        for (var i = 0; i < args.Count; i++) locals[i] = args[i];
        _frames.Add(new ComputeFrameState
        {
            Unit = root,
            Pc = 0,
            StackBase = 0,
            ReturnPc = 0,
            Locals = locals
        });
    }

    private ComputeVm(ComputeProgram program, IComputeHost host, IReadOnlyList<ComputeFrameState> frames,
        IReadOnlyList<ComputeValue> stack, long steps, IReadOnlyList<ComputeEffectRecord> effects,
        ComputeStatus status, string code, string message, ComputeValue? output)
    {
        _program = program;
        _host = host;
        foreach (var unit in program.Units) _units[unit.CalcId] = unit;
        _frames.AddRange(frames);
        _stack.AddRange(stack);
        _effects.AddRange(effects);
        _steps = steps;
        _status = status;
        _code = code;
        _message = message;
        _output = output;
    }

    public ComputeStatus Status => _status;
    public string Code => _code;
    public string Message => _message;
    public long Steps => _steps;
    public ComputeValue? Output => _output;
    public IReadOnlyList<ComputeEffectRecord> Effects => _effects;
    public ComputeProgram Program => _program;

    public void Cancel() => _cancelled = true;

    public ComputeResult Run(int slice = 1000)
    {
        while (true)
        {
            var result = Step(slice);
            if (result.Status != ComputeStatus.Suspended) return result;
        }
    }

    public ComputeResult Step(int slice = 1000)
    {
        if (slice <= 0) throw new ArgumentOutOfRangeException(nameof(slice));
        if (_status is not (ComputeStatus.Ready or ComputeStatus.Suspended))
            return Result();

        _status = ComputeStatus.Suspended;
        var executed = 0;
        while (executed < slice)
        {
            if (_cancelled)
            {
                Fail(ComputeStatus.Cancelled, ComputeCodes.Cancelled, "执行被取消");
                return Result();
            }

            if (_frames.Count == 0)
            {
                Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded, "无活动栈帧");
                return Result();
            }

            var frame = _frames[^1];
            if (frame.Pc < 0 || frame.Pc >= frame.Unit.Code.Count)
            {
                Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                    $"单元 '{frame.Unit.CalcId}' 的指令指针越界");
                return Result();
            }

            var instruction = frame.Unit.Code[frame.Pc];
            var cost = InstructionCost(instruction);
            if (frame.Unit.Budget.HasValue && frame.Steps + cost > frame.Unit.Budget.Value)
            {
                frame.Steps = frame.Unit.Budget.Value;
                _steps = _frames.Sum(f => f.Steps);
                Fail(ComputeStatus.BudgetExceeded, ComputeCodes.BudgetExceeded,
                    $"单元 '{frame.Unit.CalcId}' 预算耗尽（budget={frame.Unit.Budget.Value}）");
                return Result();
            }

            frame.Pc++;
            frame.Steps += cost;
            _steps += cost;
            executed++;
            _host.Observer?.OnInstruction(frame.Unit.CalcId, frame.Pc - 1, _steps);

            if (!Execute(frame, instruction))
            {
                return Result();
            }

            if (_status == ComputeStatus.Completed) return Result();
        }

        return Result();
    }

    private ComputeResult Result()
        => new(_status, _output, _code, _message, _steps);

    private bool Execute(ComputeFrameState frame, ComputeInstruction instruction)
    {
        switch (instruction.Op)
        {
            case "push_int":
                _stack.Add(ComputeValue.Int(instruction.Number));
                return true;
            case "push_dec":
                _stack.Add(ComputeValue.Decimal(decimal.Parse(instruction.Text, CultureInfo.InvariantCulture)));
                return true;
            case "push_str":
                _stack.Add(ComputeValue.String(instruction.Text));
                return true;
            case "push_bool":
                _stack.Add(ComputeValue.Bool(instruction.Number != 0));
                return true;
            case "load":
            {
                var value = frame.Locals[instruction.Count];
                if (value is null)
                {
                    Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                        $"读取未赋值的局部槽 {instruction.Count}");
                    return false;
                }

                _stack.Add(value);
                return true;
            }
            case "store":
            {
                if (_stack.Count == 0) return StackUnderflow();
                frame.Locals[instruction.Count] = Pop();
                return true;
            }
            case "bin":
                return ExecuteBinary(frame, instruction);
            case "cmp":
                return ExecuteCompare(frame, instruction);
            case "un":
                return ExecuteUnary(frame, instruction);
            case "jump":
                frame.Pc = instruction.Count;
                return true;
            case "jump_if_false":
            {
                var value = Pop();
                if (value.Kind != ComputeValueKind.Bool)
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        "jump_if_false 需要 bool");
                    return false;
                }

                if (!value.BoolValue) frame.Pc = instruction.Count;
                return true;
            }
            case "call":
                return ExecuteCall(frame, instruction);
            case "builtin":
                return ExecuteBuiltin(frame, instruction);
            case "convert":
                return ExecuteConvert(frame, instruction);
            case "list":
            {
                var items = new List<ComputeValue>(instruction.Count);
                for (var i = 0; i < instruction.Count; i++) items.Add(Pop());
                items.Reverse();
                var value = ComputeValue.List(items);
                if (!EnsureHeap(frame.Unit, value)) return false;
                _stack.Add(value);
                return true;
            }
            case "map":
            {
                var entries = new List<KeyValuePair<string, ComputeValue>>(instruction.Count);
                for (var i = 0; i < instruction.Count; i++)
                {
                    var value = Pop();
                    var key = Pop();
                    if (key.Kind != ComputeValueKind.String)
                    {
                        Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                            "map 键必须是 string");
                        return false;
                    }

                    entries.Add(new KeyValuePair<string, ComputeValue>(key.Text, value));
                }

                entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                var value = ComputeValue.Map(entries);
                if (!EnsureHeap(frame.Unit, value)) return false;
                _stack.Add(value);
                return true;
            }
            case "ret":
            {
                if (_stack.Count == 0) return StackUnderflow();
                return Return(frame, Pop());
            }
            case "ret_unit":
                return Return(frame, null);
            default:
                Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                    $"未知指令 '{instruction.Op}'");
                return false;
        }
    }

    private bool ExecuteBinary(ComputeFrameState frame, ComputeInstruction instruction)
    {
        if (_stack.Count < 2) return StackUnderflow();
        var right = Pop();
        var left = Pop();
        ComputeValue result;
        try
        {
            switch (instruction.Text)
            {
                case "add":
                    if (left.Kind == ComputeValueKind.Int && right.Kind == ComputeValueKind.Int)
                        result = ComputeValue.Int(checked(left.IntValue + right.IntValue));
                    else if (left.Kind == ComputeValueKind.Decimal && right.Kind == ComputeValueKind.Decimal)
                        result = ComputeValue.Decimal(ComputeHostBoundary.Quantize(checked(left.DecimalValue + right.DecimalValue)));
                    else if (left.Kind == ComputeValueKind.String && right.Kind == ComputeValueKind.String)
                        result = ComputeValue.String(left.Text + right.Text);
                    else return TypeError("add");
                    break;
                case "sub":
                    if (left.Kind == ComputeValueKind.Int && right.Kind == ComputeValueKind.Int)
                        result = ComputeValue.Int(checked(left.IntValue - right.IntValue));
                    else if (left.Kind == ComputeValueKind.Decimal && right.Kind == ComputeValueKind.Decimal)
                        result = ComputeValue.Decimal(ComputeHostBoundary.Quantize(checked(left.DecimalValue - right.DecimalValue)));
                    else return TypeError("sub");
                    break;
                case "mul":
                    if (left.Kind == ComputeValueKind.Int && right.Kind == ComputeValueKind.Int)
                        result = ComputeValue.Int(checked(left.IntValue * right.IntValue));
                    else if (left.Kind == ComputeValueKind.Decimal && right.Kind == ComputeValueKind.Decimal)
                        result = ComputeValue.Decimal(ComputeHostBoundary.Quantize(checked(left.DecimalValue * right.DecimalValue)));
                    else return TypeError("mul");
                    break;
                case "div":
                    if (left.Kind == ComputeValueKind.Int && right.Kind == ComputeValueKind.Int)
                    {
                        if (right.IntValue == 0)
                        {
                            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "int 除法除数为 0");
                            return false;
                        }

                        if (left.IntValue % right.IntValue != 0)
                        {
                            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                                $"int 除法必须整除：{left.IntValue}/{right.IntValue}");
                            return false;
                        }

                        result = ComputeValue.Int(left.IntValue / right.IntValue);
                    }
                    else if (left.Kind == ComputeValueKind.Decimal && right.Kind == ComputeValueKind.Decimal)
                    {
                        if (right.DecimalValue == 0m)
                        {
                            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "decimal 除法除数为 0");
                            return false;
                        }

                        result = ComputeValue.Decimal(ComputeHostBoundary.Quantize(left.DecimalValue / right.DecimalValue));
                    }
                    else return TypeError("div");
                    break;
                case "mod":
                    if (left.Kind == ComputeValueKind.Int && right.Kind == ComputeValueKind.Int)
                    {
                        if (right.IntValue == 0)
                        {
                            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "mod 除数为 0");
                            return false;
                        }

                        result = ComputeValue.Int(left.IntValue % right.IntValue);
                    }
                    else if (left.Kind == ComputeValueKind.Decimal && right.Kind == ComputeValueKind.Decimal)
                    {
                        if (right.DecimalValue == 0m)
                        {
                            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "mod 除数为 0");
                            return false;
                        }

                        result = ComputeValue.Decimal(ComputeHostBoundary.Quantize(left.DecimalValue % right.DecimalValue));
                    }
                    else return TypeError("mod");
                    break;
                default:
                    Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                        $"未知二元运算 '{instruction.Text}'");
                    return false;
            }
        }
        catch (OverflowException)
        {
            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "数值溢出");
            return false;
        }

        _stack.Add(result);
        return true;
    }

    private bool ExecuteCompare(ComputeFrameState frame, ComputeInstruction instruction)
    {
        if (_stack.Count < 2) return StackUnderflow();
        var right = Pop();
        var left = Pop();
        bool result;
        switch (instruction.Text)
        {
            case "eq":
            case "ne":
                result = CanonicalEquals(left, right);
                if (instruction.Text == "ne") result = !result;
                break;
            case "lt":
            case "le":
            case "gt":
            case "ge":
                var order = CompareValues(left, right, instruction.Text);
                if (order is null)
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        $"不可比较的值类型 {left.Kind}/{right.Kind}");
                    return false;
                }

                result = instruction.Text switch
                {
                    "lt" => order == CompareResult.Less,
                    "le" => order is CompareResult.Less or CompareResult.Equal,
                    "gt" => order == CompareResult.Greater,
                    _ => order is CompareResult.Greater or CompareResult.Equal
                };
                break;
            default:
                Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                    $"未知比较运算 '{instruction.Text}'");
                return false;
        }

        _stack.Add(ComputeValue.Bool(result));
        return true;
    }

    private enum CompareResult
    {
        Less,
        Equal,
        Greater
    }

    private static CompareResult? CompareValues(ComputeValue left, ComputeValue right, string op)
    {
        if (left.Kind == ComputeValueKind.Int && right.Kind == ComputeValueKind.Int)
            return left.IntValue < right.IntValue ? CompareResult.Less
                : left.IntValue > right.IntValue ? CompareResult.Greater : CompareResult.Equal;
        if (left.Kind == ComputeValueKind.Decimal && right.Kind == ComputeValueKind.Decimal)
            return left.DecimalValue < right.DecimalValue ? CompareResult.Less
                : left.DecimalValue > right.DecimalValue ? CompareResult.Greater : CompareResult.Equal;
        if (left.Kind == ComputeValueKind.String && right.Kind == ComputeValueKind.String)
        {
            var compare = string.CompareOrdinal(left.Text, right.Text);
            return compare < 0 ? CompareResult.Less : compare > 0 ? CompareResult.Greater : CompareResult.Equal;
        }

        return null;
    }

    private static bool CanonicalEquals(ComputeValue left, ComputeValue right)
    {
        if (left.Kind != right.Kind) return false;
        return left.Kind switch
        {
            ComputeValueKind.Int => left.IntValue == right.IntValue,
            ComputeValueKind.Decimal => left.DecimalValue == right.DecimalValue,
            ComputeValueKind.Bool => left.BoolValue == right.BoolValue,
            ComputeValueKind.String => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            ComputeValueKind.Unit => true,
            ComputeValueKind.Host => string.Equals(left.HostValue?.CanonicalText, right.HostValue?.CanonicalText,
                StringComparison.Ordinal),
            ComputeValueKind.List => left.Items.Count == right.Items.Count
                                     && left.Items.Zip(right.Items).All(p => CanonicalEquals(p.First, p.Second)),
            ComputeValueKind.Map => left.Entries.Count == right.Entries.Count
                                    && left.Entries.Zip(right.Entries).All(p =>
                                        string.Equals(p.First.Key, p.Second.Key, StringComparison.Ordinal)
                                        && CanonicalEquals(p.First.Value, p.Second.Value)),
            _ => false
        };
    }

    private bool ExecuteUnary(ComputeFrameState frame, ComputeInstruction instruction)
    {
        if (_stack.Count == 0) return StackUnderflow();
        var value = Pop();
        try
        {
            switch (instruction.Text)
            {
                case "neg":
                    if (value.Kind == ComputeValueKind.Int)
                    {
                        _stack.Add(ComputeValue.Int(checked(-value.IntValue)));
                        return true;
                    }

                    if (value.Kind == ComputeValueKind.Decimal)
                    {
                        _stack.Add(ComputeValue.Decimal(-value.DecimalValue));
                        return true;
                    }

                    return TypeError("neg");
                case "not":
                    if (value.Kind == ComputeValueKind.Bool)
                    {
                        _stack.Add(ComputeValue.Bool(!value.BoolValue));
                        return true;
                    }

                    return TypeError("not");
                default:
                    Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                        $"未知一元运算 '{instruction.Text}'");
                    return false;
            }
        }
        catch (OverflowException)
        {
            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "数值溢出");
            return false;
        }
    }

    private bool ExecuteConvert(ComputeFrameState frame, ComputeInstruction instruction)
    {
        if (_stack.Count == 0) return StackUnderflow();
        var value = Pop();
        switch (instruction.Text, instruction.Aux)
        {
            case ("host", "int"):
                if (value.Kind != ComputeValueKind.Host) return TypeError("to_int");
                return PushHostInt(value.HostValue!);
            case ("host", "decimal"):
            {
                if (value.Kind != ComputeValueKind.Host) return TypeError("to_decimal");
                var quantized = HostDecimal(value.HostValue!);
                if (quantized is null)
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        $"host 值 '{value.HostValue!.CanonicalText}' 不是 decimal");
                    return false;
                }

                _stack.Add(ComputeValue.Decimal(instruction.Rule == "half_even"
                    ? ComputeHostBoundary.Quantize(quantized.Value)
                    : quantized.Value));
                return true;
            }
            case ("host", "string"):
                if (value.Kind != ComputeValueKind.Host) return TypeError("str");
                _stack.Add(ComputeValue.String(value.HostValue!.CanonicalText));
                return true;
            case ("int", "host"):
                if (value.Kind != ComputeValueKind.Int) return TypeError("to_host");
                _stack.Add(ComputeValue.Host(ComputeHostValue.FromInt(value.IntValue)));
                return true;
            case ("decimal", "host"):
                if (value.Kind != ComputeValueKind.Decimal) return TypeError("to_host");
                _stack.Add(ComputeValue.Host(ComputeHostValue.FromDecimal(value.DecimalValue)));
                return true;
            case ("bool", "host"):
                if (value.Kind != ComputeValueKind.Bool) return TypeError("to_host");
                _stack.Add(ComputeValue.Host(ComputeHostValue.FromBool(value.BoolValue)));
                return true;
            case ("string", "host"):
                if (value.Kind != ComputeValueKind.String) return TypeError("to_host");
                _stack.Add(ComputeValue.Host(ComputeHostValue.FromString(value.Text)));
                return true;
            default:
                Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                    $"未知转换 {instruction.Text}->{instruction.Aux}");
                return false;
        }
    }

    private bool PushHostInt(ComputeHostValue value)
    {
        switch (value.Kind)
        {
            case ComputeHostValueKind.Int:
                _stack.Add(ComputeValue.Int(long.Parse(value.CanonicalText, CultureInfo.InvariantCulture)));
                return true;
            case ComputeHostValueKind.Decimal:
                var dec = decimal.Parse(value.CanonicalText, CultureInfo.InvariantCulture);
                if (dec != decimal.Truncate(dec))
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        $"host decimal '{value.CanonicalText}' 不是整数");
                    return false;
                }

                _stack.Add(ComputeValue.Int((long)dec));
                return true;
            default:
                Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                    $"host 值 '{value.CanonicalText}' 不能转 int");
                return false;
        }
    }

    private static decimal? HostDecimal(ComputeHostValue value) => value.Kind switch
    {
        ComputeHostValueKind.Int => decimal.Parse(value.CanonicalText, CultureInfo.InvariantCulture),
        ComputeHostValueKind.Decimal => decimal.Parse(value.CanonicalText, CultureInfo.InvariantCulture),
        _ => null
    };

    private bool ExecuteCall(ComputeFrameState frame, ComputeInstruction instruction)
    {
        var callee = _units.GetValueOrDefault(instruction.Text);
        if (callee is null)
        {
            Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                $"调用未知单元 '{instruction.Text}'");
            return false;
        }

        if (instruction.Count > _stack.Count) return StackUnderflow();
        var args = new ComputeValue?[instruction.Count];
        for (var i = instruction.Count - 1; i >= 0; i--) args[i] = Pop();

        if (_frames.Count >= callee.Depth)
        {
            Fail(ComputeStatus.StackResourceExceeded, ComputeCodes.StackResourceExceeded,
                $"调用深度超过单元 '{callee.CalcId}' 的 depth={callee.Depth}");
            return false;
        }

        var locals = new ComputeValue?[instruction.Count + callee.LocalCount];
        for (var i = 0; i < args.Length; i++) locals[i] = args[i];
        _frames.Add(new ComputeFrameState
        {
            Unit = callee,
            Pc = 0,
            StackBase = _stack.Count,
            ReturnPc = frame.Pc,
            Locals = locals
        });
        return true;
    }

    private bool Return(ComputeFrameState frame, ComputeValue? value)
    {
        _frames.RemoveAt(_frames.Count - 1);
        while (_stack.Count > frame.StackBase) _stack.RemoveAt(_stack.Count - 1);
        if (_frames.Count == 0)
        {
            _status = ComputeStatus.Completed;
            _output = value ?? ComputeValue.UnitValue;
            return true;
        }

        if (value is not null) _stack.Add(value);
        _frames[^1].Pc = frame.ReturnPc;
        return true;
    }

    private bool ExecuteBuiltin(ComputeFrameState frame, ComputeInstruction instruction)
    {
        var name = instruction.Text;
        var argc = instruction.Count;
        if (_stack.Count < argc) return StackUnderflow();
        var args = new ComputeValue[argc];
        for (var i = argc - 1; i >= 0; i--) args[i] = Pop();

        switch (name)
        {
            case "len":
            {
                var value = args[0];
                long length = value.Kind switch
                {
                    ComputeValueKind.String => value.Text.Length,
                    ComputeValueKind.List => value.Items.Count,
                    ComputeValueKind.Map => value.Entries.Count,
                    _ => -1
                };
                if (length < 0) return TypeError("len");
                _stack.Add(ComputeValue.Int(length));
                return true;
            }
            case "get":
            {
                if (args.Length is not (2 or 3))
                {
                    Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded, "get 参数个数");
                    return false;
                }

                ComputeValue? fallback = args.Length == 3 ? args[2] : null;
                if (args[0].Kind == ComputeValueKind.List)
                {
                    if (args[1].Kind != ComputeValueKind.Int) return TypeError("get");
                    var index = args[1].IntValue;
                    if (index < 0 || index >= args[0].Items.Count)
                    {
                        if (fallback is not null)
                        {
                            _stack.Add(fallback);
                            return true;
                        }

                        Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                            $"get 列表越界：{index}/{args[0].Items.Count}");
                        return false;
                    }

                    _stack.Add(args[0].Items[(int)index]);
                    return true;
                }

                if (args[0].Kind == ComputeValueKind.Map)
                {
                    if (args[1].Kind != ComputeValueKind.String) return TypeError("get");
                    ComputeValue? hit = null;
                    foreach (var entry in args[0].Entries)
                    {
                        if (string.Equals(entry.Key, args[1].Text, StringComparison.Ordinal))
                        {
                            hit = entry.Value;
                            break;
                        }
                    }

                    if (hit is not null) return Push(hit);
                    if (fallback is not null)
                    {
                        _stack.Add(fallback);
                        return true;
                    }

                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        $"get 缺失键 '{args[1].Text}'");
                    return false;
                }

                return TypeError("get");
            }
            case "append":
            {
                if (args[0].Kind != ComputeValueKind.List) return TypeError("append");
                var items = args[0].Items.ToList();
                items.Add(args[1]);
                var value = ComputeValue.List(items);
                if (!EnsureHeap(frame.Unit, value)) return false;
                _stack.Add(value);
                return true;
            }
            case "put":
            {
                if (args[0].Kind != ComputeValueKind.Map || args[1].Kind != ComputeValueKind.String)
                    return TypeError("put");
                var entries = args[0].Entries
                    .Where(e => !string.Equals(e.Key, args[1].Text, StringComparison.Ordinal)).ToList();
                entries.Add(new KeyValuePair<string, ComputeValue>(args[1].Text, args[2]));
                entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                var value = ComputeValue.Map(entries);
                if (!EnsureHeap(frame.Unit, value)) return false;
                _stack.Add(value);
                return true;
            }
            case "has":
            {
                if (args[0].Kind != ComputeValueKind.Map || args[1].Kind != ComputeValueKind.String)
                    return TypeError("has");
                _stack.Add(ComputeValue.Bool(
                    args[0].Entries.Any(e => string.Equals(e.Key, args[1].Text, StringComparison.Ordinal)));
                return true;
            }
            case "mod":
                return ExecuteBinaryFromValues(frame, args[0], args[1], "mod");
            case "abs":
            {
                var value = args[0];
                if (value.Kind == ComputeValueKind.Int)
                {
                    _stack.Add(ComputeValue.Int(Math.Abs(value.IntValue)));
                    return true;
                }

                if (value.Kind == ComputeValueKind.Decimal)
                {
                    _stack.Add(ComputeValue.Decimal(Math.Abs(value.DecimalValue)));
                    return true;
                }

                return TypeError("abs");
            }
            case "min":
            case "max":
            {
                if (args[0].Kind != args[1].Kind
                    || args[0].Kind is not (ComputeValueKind.Int or ComputeValueKind.Decimal))
                    return TypeError(name);

                var compare = CompareValues(args[0], args[1], name);
                if (compare is null) return TypeError(name);
                var take = name == "min"
                    ? compare == CompareResult.Less
                    : compare == CompareResult.Greater;
                _stack.Add(take ? args[0] : args[1]);
                return true;
            }
            case "to_int":
            {
                var value = args[0];
                if (value.Kind == ComputeValueKind.Decimal)
                {
                    _stack.Add(ComputeValue.Int((long)decimal.Truncate(value.DecimalValue)));
                    return true;
                }

                return TypeError("to_int");
            }
            case "to_decimal":
            {
                var value = args[0];
                if (value.Kind == ComputeValueKind.Int)
                {
                    _stack.Add(ComputeValue.Decimal(value.IntValue));
                    return true;
                }

                return TypeError("to_decimal");
            }
            case "str":
            {
                if (args[0].Kind is ComputeValueKind.List or ComputeValueKind.Map
                    or ComputeValueKind.Unit or ComputeValueKind.Host) return TypeError("str");
                _stack.Add(ComputeValue.String(args[0].CanonicalText()));
                return true;
            }
            case "parse_int":
            {
                if (args[0].Kind != ComputeValueKind.String) return TypeError("parse_int");
                if (!long.TryParse(args[0].Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        $"parse_int 失败：'{args[0].Text}'");
                    return false;
                }

                _stack.Add(ComputeValue.Int(parsed));
                return true;
            }
            case "parse_decimal":
            {
                if (args[0].Kind != ComputeValueKind.String) return TypeError("parse_decimal");
                if (!decimal.TryParse(args[0].Text, NumberStyles.Number, CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        $"parse_decimal 失败：'{args[0].Text}'");
                    return false;
                }

                _stack.Add(ComputeValue.Decimal(ComputeHostBoundary.Quantize(parsed)));
                return true;
            }
            case "key_read":
                return ExecuteKeyRead(frame, args[0], args[1]);
            case "key_write":
                return ExecuteKeyWrite(frame, args[0], args[1], args[2]);
            case "emit":
                return ExecuteEmit(frame, args[0], args[1]);
            case "clock_now":
            {
                if (!EnsureEffect(frame.Unit, ComputeEffect.Clock, "clock_now")) return false;
                var port = _host.Capability;
                if (port is null) return EffectDenied("clock");
                try
                {
                    var now = port.ClockNowUnixMs();
                    _effects.Add(new ComputeEffectRecord("clock", "now", now.ToString(CultureInfo.InvariantCulture), ""));
                    _stack.Add(ComputeValue.Int(now));
                    return true;
                }
                catch (Exception ex)
                {
                    Fail(ComputeStatus.PortFailure, ComputeCodes.PortFailure, $"clock 端口异常：{ex.Message}");
                    return false;
                }
            }
            case "rand_int":
            {
                if (args[0].Kind != ComputeValueKind.Int || args[1].Kind != ComputeValueKind.Int)
                    return TypeError("rand_int");
                if (!EnsureEffect(frame.Unit, ComputeEffect.Random, "rand_int")) return false;
                var port = _host.Capability;
                if (port is null) return EffectDenied("random");
                if (args[1].IntValue <= args[0].IntValue)
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError,
                        "rand_int 的 [lo,hi) 必须 hi>lo");
                    return false;
                }

                try
                {
                    var value = port.NextRandomLong(args[0].IntValue, args[1].IntValue);
                    _effects.Add(new ComputeEffectRecord("random", "next",
                        value.ToString(CultureInfo.InvariantCulture), ""));
                    _stack.Add(ComputeValue.Int(value));
                    return true;
                }
                catch (Exception ex)
                {
                    Fail(ComputeStatus.PortFailure, ComputeCodes.PortFailure, $"random 端口异常：{ex.Message}");
                    return false;
                }
            }
            default:
                Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded,
                    $"未知内建 '{name}'");
                return false;
        }
    }

    private bool ExecuteBinaryFromValues(ComputeFrameState frame, ComputeValue left, ComputeValue right, string fn)
    {
        try
        {
            if (left.Kind == ComputeValueKind.Int && right.Kind == ComputeValueKind.Int)
            {
                if (right.IntValue == 0)
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "mod 除数为 0");
                    return false;
                }

                _stack.Add(ComputeValue.Int(left.IntValue % right.IntValue));
                return true;
            }

            if (left.Kind == ComputeValueKind.Decimal && right.Kind == ComputeValueKind.Decimal)
            {
                if (right.DecimalValue == 0m)
                {
                    Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "mod 除数为 0");
                    return false;
                }

                _stack.Add(ComputeValue.Decimal(ComputeHostBoundary.Quantize(left.DecimalValue % right.DecimalValue)));
                return true;
            }

            return TypeError(fn);
        }
        catch (OverflowException)
        {
            Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, "数值溢出");
            return false;
        }
    }

    private bool ExecuteKeyRead(ComputeFrameState frame, ComputeValue space, ComputeValue coordinate)
    {
        if (space.Kind != ComputeValueKind.String || coordinate.Kind != ComputeValueKind.String)
            return TypeError("$space.coordinate");
        if (!EnsureEffect(frame.Unit, ComputeEffect.KeyRead, "ks.read")) return false;
        var port = _host.KeySpace;
        if (port is null) return EffectDenied("ks.read");
        try
        {
            var value = port.Read(space.Text, coordinate.Text);
            if (value is null)
            {
                Fail(ComputeStatus.PortFailure, ComputeCodes.PortFailure,
                    $"键空间读取返回空：{space.Text}.{coordinate.Text}");
                return false;
            }

            _effects.Add(new ComputeEffectRecord("ks.read", space.Text + "." + coordinate.Text,
                value.CanonicalText, value.Kind.ToString().ToLowerInvariant()));
            _stack.Add(ComputeValue.Host(value));
            return true;
        }
        catch (Exception ex)
        {
            Fail(ComputeStatus.PortFailure, ComputeCodes.PortFailure, $"键空间读取异常：{ex.Message}");
            return false;
        }
    }

    private bool ExecuteKeyWrite(ComputeFrameState frame, ComputeValue space, ComputeValue coordinate,
        ComputeValue value)
    {
        if (space.Kind != ComputeValueKind.String || coordinate.Kind != ComputeValueKind.String
            || value.Kind != ComputeValueKind.Host)
            return TypeError("write_key");
        if (!EnsureEffect(frame.Unit, ComputeEffect.KeyWrite, "ks.write")) return false;
        var port = _host.KeySpace;
        if (port is null) return EffectDenied("ks.write");
        try
        {
            port.Write(space.Text, coordinate.Text, value.HostValue!);
            _effects.Add(new ComputeEffectRecord("ks.write", space.Text + "." + coordinate.Text,
                value.HostValue!.CanonicalText, value.HostValue.Kind.ToString().ToLowerInvariant()));
            _stack.Add(ComputeValue.UnitValue);
            return true;
        }
        catch (Exception ex)
        {
            Fail(ComputeStatus.PortFailure, ComputeCodes.PortFailure, $"键空间写入异常：{ex.Message}");
            return false;
        }
    }

    private bool ExecuteEmit(ComputeFrameState frame, ComputeValue channel, ComputeValue value)
    {
        if (channel.Kind != ComputeValueKind.String || value.Kind != ComputeValueKind.Host)
            return TypeError("emit");
        if (!EnsureEffect(frame.Unit, ComputeEffect.Emit, "emit")) return false;
        var port = _host.Emit;
        if (port is null) return EffectDenied("emit");
        try
        {
            port.Emit(channel.Text, value.HostValue!);
            _effects.Add(new ComputeEffectRecord("emit", channel.Text, value.HostValue!.CanonicalText,
                value.HostValue.Kind.ToString().ToLowerInvariant()));
            _stack.Add(ComputeValue.UnitValue);
            return true;
        }
        catch (Exception ex)
        {
            Fail(ComputeStatus.PortFailure, ComputeCodes.PortFailure, $"emit 端口异常：{ex.Message}");
            return false;
        }
    }

    private bool EnsureEffect(ComputeUnitIr unit, ComputeEffect effect, string target)
    {
        if (unit.Purity == ComputePurityPolicy.Strict)
        {
            Fail(ComputeStatus.EffectDenied, ComputeCodes.EffectDenied,
                $"pure=strict 单元 '{unit.CalcId}' 禁止效果 {target}");
            return false;
        }

        if (!unit.Effects.Contains(effect))
        {
            Fail(ComputeStatus.EffectDenied, ComputeCodes.EffectDenied,
                $"单元 '{unit.CalcId}' 未声明效果 {target}");
            return false;
        }

        return true;
    }

    private bool EffectDenied(string target)
    {
        Fail(ComputeStatus.EffectDenied, ComputeCodes.EffectDenied, $"未提供宿主端口：{target}");
        return false;
    }

    private bool EnsureHeap(ComputeUnitIr unit, ComputeValue value)
    {
        if (value.SizeUnits() > unit.Heap)
        {
            Fail(ComputeStatus.HeapResourceExceeded, ComputeCodes.HeapResourceExceeded,
                $"单元 '{unit.CalcId}' 单值单元数 {value.SizeUnits()} 超过 heap={unit.Heap}");
            return false;
        }

        return true;
    }

    private bool Push(ComputeValue value)
    {
        _stack.Add(value);
        return true;
    }

    private bool StackUnderflow()
    {
        Fail(ComputeStatus.StateDiscarded, ComputeCodes.StateDiscarded, "操作数栈下溢");
        return false;
    }

    private bool TypeError(string operation)
    {
        Fail(ComputeStatus.ArithmeticError, ComputeCodes.ArithmeticError, $"运行时类型不匹配：{operation}");
        return false;
    }

    private ComputeValue Pop()
    {
        var value = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return value;
    }

    private static long InstructionCost(ComputeInstruction instruction)
        => instruction.Op switch
        {
            "builtin" => instruction.Text switch
            {
                "append" or "put" or "emit" or "key_write" or "parse_int" or "parse_decimal" => 2,
                _ => 1
            },
            _ => 1
        };

    private void Fail(ComputeStatus status, string code, string message)
    {
        _status = status;
        _code = code;
        _message = message;
    }

    public ComputeSnapshot ExportSnapshot()
        => new(
            ComputeSnapshot.LatestVersion,
            ComputeProgram.VmVersion,
            ComputeProgram.BytecodeVersion,
            _program.Hash,
            _status,
            _steps,
            _frames.Select(f => new ComputeSnapshotFrame(f.Unit.CalcId, f.Pc, f.StackBase, f.ReturnPc,
                f.Steps, f.Locals.ToArray())).ToList(),
            _stack.ToList(),
            _effects.ToList());

    public static ComputeSnapshotLoadResult TryResume(ComputeProgram program, IComputeHost? host,
        string snapshotJson, out ComputeVm? vm)
    {
        vm = null;
        var decoded = ComputeSnapshotCodec.TryDecode(snapshotJson, program);
        if (!decoded.Accepted)
        {
            return new ComputeSnapshotLoadResult(false, null, decoded.Code, decoded.Message,
                ComputeCodes.StateDiscarded);
        }

        var snapshot = decoded.Snapshot!;
        var frames = new List<ComputeFrameState>();
        foreach (var saved in snapshot.Frames)
        {
            var unit = program.FindUnit(saved.Unit);
            if (unit is null || saved.Pc < 0 || saved.Pc > unit.Code.Count
                || saved.Locals.Count != unit.Arguments.Count + unit.LocalCount)
            {
                return new ComputeSnapshotLoadResult(false, null, ComputeCodes.StateDiscarded,
                    "快照结构与程序不一致（拒绝并标记）", ComputeCodes.StateDiscarded);
            }

            frames.Add(new ComputeFrameState
            {
                Unit = unit,
                Pc = saved.Pc,
                StackBase = saved.StackBase,
                ReturnPc = saved.ReturnPc,
                Steps = saved.Steps,
                Locals = saved.Locals.ToArray()
            });
        }

        if (frames.Count == 0)
        {
            return new ComputeSnapshotLoadResult(false, null, ComputeCodes.StateDiscarded,
                "快照没有活动帧（拒绝并标记）", ComputeCodes.StateDiscarded);
        }

        var status = snapshot.Status == ComputeStatus.Ready ? ComputeStatus.Suspended : snapshot.Status;
        vm = new ComputeVm(program, host ?? ComputeHost.Empty, frames, snapshot.Stack, snapshot.Steps,
            snapshot.Effects, status, "", "", null);
        return new ComputeSnapshotLoadResult(true, snapshot, "", "", null);
    }
}

public sealed record ComputeSnapshot(int SnapshotVersion, int VmVersion, int BytecodeVersion,
    string ProgramHash, ComputeStatus Status, long Steps, IReadOnlyList<ComputeSnapshotFrame> Frames,
    IReadOnlyList<ComputeValue> Stack, IReadOnlyList<ComputeEffectRecord> Effects)
{
    public const int LatestVersion = 1;
}

public sealed record ComputeSnapshotFrame(string Unit, int Pc, int StackBase, int ReturnPc,
    long Steps, IReadOnlyList<ComputeValue?> Locals);

public sealed record ComputeSnapshotLoadResult(bool Accepted, ComputeSnapshot? Snapshot, string Code,
    string Message, string? MarkCode);
