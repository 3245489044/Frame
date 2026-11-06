using System.Globalization;
using Plate.DslMethod.Model;

namespace Usharp.Compute;

internal sealed class ComputeLoweringResult
{
    public ComputeUnitIr? Program { get; set; }
    public List<ComputeDiagnostic> Diagnostics { get; } = new();
    public List<string> Callees { get; } = new();
}

internal sealed class ComputeLowering
{
    private readonly ComputeUnit _unit;
    private readonly IReadOnlyDictionary<string, ComputeUnitHeader> _headers;
    private readonly Func<string, string, bool>? _coordinateExists;

    public ComputeLowering(ComputeUnit unit, IReadOnlyDictionary<string, ComputeUnitHeader> headers,
        Func<string, string, bool>? coordinateExists)
    {
        _unit = unit;
        _headers = headers;
        _coordinateExists = coordinateExists;
    }

    public ComputeLoweringResult Run()
    {
        var result = new ComputeLoweringResult();
        var emitter = new Emitter(_unit, _headers, _coordinateExists, result);
        result.Program = emitter.Run();
        return result;
    }

    private sealed record Binding(int Slot, ComputeType Type, bool Mutable);

    private sealed class Emitter
    {
        private static readonly HashSet<string> EffectBuiltins = new(StringComparer.Ordinal)
        {
            "emit", "clock_now", "rand_int", "key_read", "key_write", "read_key"
        };

        private readonly ComputeUnit _unit;
        private readonly ComputeUnitHeader _header;
        private readonly IReadOnlyDictionary<string, ComputeUnitHeader> _headers;
        private readonly Func<string, string, bool>? _coordinateExists;
        private readonly ComputeLoweringResult _result;

        private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);
        private readonly List<ComputeInstruction> _code = new();
        private readonly List<ComputeSourceMapEntry> _sourceMap = new();
        private readonly List<ComputeLoopMeta> _loops = new();
        private readonly List<ComputeDiagnostic> _diagnostics = new();
        private readonly List<ComputeInstruction> _instructions = new();
        private int _nextSlot;
        private bool _failed;

        public Emitter(ComputeUnit unit, IReadOnlyDictionary<string, ComputeUnitHeader> headers,
            Func<string, string, bool>? coordinateExists, ComputeLoweringResult result)
        {
            _unit = unit;
            _header = unit.Header;
            _headers = headers;
            _coordinateExists = coordinateExists;
            _result = result;
        }

        public ComputeUnitIr? Run()
        {
            _nextSlot = _header.Arguments.Count;
            foreach (var argument in _header.Arguments)
            {
                _bindings[argument.Name] = new Binding(_bindings.Count, argument.Type, false);
                _assigned.Add(argument.Name);
            }

            if (_header.Kind == ComputeUnitKind.Expr)
            {
                if (_unit.Body.Count != 1 || _unit.Body[0] is not ComputeReturnStatement { Value: not null } only)
                {
                    Error(ComputeCodes.BoundaryViolation,
                        "kind=expr 的正文必须且只能是一条 'return <表达式>'", _header.LineNumber);
                    Flush();
                    return null;
                }

                EmitReturn(only);
            }
            else
            {
                EmitStatements(_unit.Body);
            }

            if (_header.Kind == ComputeUnitKind.Proc && (_code.Count == 0 || LacksTerminalReturn()))
            {
                Emit(ComputeInstruction.ReturnUnit(), _header.LineNumber);
            }
            else if (_header.Kind == ComputeUnitKind.Fn && !AllPathsReturn(_unit.Body))
            {
                Error(ComputeCodes.ProgramNotProducible,
                    $"kind=fn 的单元 '{_header.CalcId}' 并非所有路径都 return", _header.LineNumber);
            }

            _result.Callees.AddRange(_callees.Distinct(StringComparer.Ordinal)
                .Where(id => _headers.ContainsKey(id)));

            Flush();
            if (_failed) return null;

            return new ComputeUnitIr
            {
                CalcId = _header.CalcId,
                Kind = _header.Kind,
                Arguments = _header.Arguments,
                ReturnType = _header.ReturnType,
                HasReturn = _header.HasReturn,
                Purity = _header.Purity,
                Effects = _header.Effects,
                Budget = _header.EffectiveBudget,
                Slice = _header.Slice,
                Heap = _header.Heap,
                Depth = _header.EffectiveDepth,
                LocalCount = _nextSlot - _header.Arguments.Count,
                SourceFile = _header.SourceFile,
                LineNumber = _header.LineNumber,
                Code = _code,
                SourceMap = _sourceMap,
                Loops = _loops
            };
        }

        private readonly List<string> _callees = new();

        private bool LacksTerminalReturn()
            => _code.Count == 0 || _code[^1].Op is not ("ret" or "ret_unit");

        private void Flush()
        {
            _result.Diagnostics.AddRange(_diagnostics);
        }

        private void EmitStatements(IReadOnlyList<ComputeStatement> statements)
        {
            foreach (var statement in statements)
            {
                EmitStatement(statement);
            }
        }

        private void EmitStatement(ComputeStatement statement)
        {
            switch (statement)
            {
                case ComputeLetStatement let:
                    EmitLet(let);
                    break;
                case ComputeSetStatement set:
                    EmitSet(set);
                    break;
                case ComputeIfStatement branch:
                    EmitIf(branch);
                    break;
                case ComputeLoopStatement loop:
                    EmitLoop(loop);
                    break;
                case ComputeWhileStatement loop:
                    EmitWhile(loop);
                    break;
                case ComputeReturnStatement ret:
                    EmitReturn(ret);
                    break;
                case ComputeCallStatement call:
                    EmitCallStatement(call);
                    break;
                default:
                    Error(ComputeCodes.SyntaxInvalid, "未知语句", statement.Line);
                    break;
            }
        }

        private void EmitLet(ComputeLetStatement statement)
        {
            if (_bindings.ContainsKey(statement.Name))
            {
                Error(ComputeCodes.BindingDuplicate,
                    $"绑定 '{statement.Name}' 与本单元已有绑定重名（禁止遮蔽）", statement.Line);
                return;
            }

            var type = EmitExpression(statement.Value);
            if (type is null) return;

            var slot = _nextSlot++;
            _bindings[statement.Name] = new Binding(slot, type, statement.Mutable);
            _assigned.Add(statement.Name);
            Emit(ComputeInstruction.Store(slot), statement.Line);
            if (type.Kind == ComputeTypeKind.Unit)
            {
                Error(ComputeCodes.TypeMismatch,
                    $"let/var 不能绑定 unit（'{statement.Name}'）", statement.Line);
            }
        }

        private void EmitSet(ComputeSetStatement statement)
        {
            var binding = _bindings.GetValueOrDefault(statement.Name);
            if (binding is null)
            {
                Error(ComputeCodes.NameUndeclared,
                    $"set 目标 '{statement.Name}' 未声明", statement.Line);
                return;
            }

            if (!binding.Mutable)
            {
                Error(ComputeCodes.WriteImmutable,
                    $"'{statement.Name}' 是不可变绑定（let/参数/循环变量），禁止 set", statement.Line);
                return;
            }

            var type = EmitExpression(statement.Value);
            if (type is null) return;
            if (type != binding.Type)
            {
                Error(ComputeCodes.TypeMismatch,
                    $"set 类型不匹配：'{statement.Name}' 是 {binding.Type.Display}，右侧是 {type.Display}",
                    statement.Line);
                return;
            }

            Emit(ComputeInstruction.Store(binding.Slot), statement.Line);
            _assigned.Add(statement.Name);
        }

        private void EmitIf(ComputeIfStatement statement)
        {
            var condition = EmitExpression(statement.Condition);
            if (condition is null) return;
            if (condition.Kind != ComputeTypeKind.Bool)
            {
                Error(ComputeCodes.TypeMismatch, "if 条件必须是 bool", statement.Line);
                return;
            }

            var jumpFalseIndex = Emit(ComputeInstruction.JumpIfFalse(0), statement.Line);
            var assignedBefore = _assigned.ToHashSet(StringComparer.Ordinal);
            var thenAssigned = assignedBefore.ToHashSet(StringComparer.Ordinal);
            EmitBlock(statement.Then, thenAssigned);
            var jumpEndIndex = Emit(ComputeInstruction.Jump(0), statement.Line);
            var elseStart = _code.Count;
            _code[jumpFalseIndex] = ComputeInstruction.JumpIfFalse(elseStart);
            _assigned.Clear();
            _assigned.UnionWith(assignedBefore);
            if (statement.Else is not null)
            {
                var elseAssigned = assignedBefore.ToHashSet(StringComparer.Ordinal);
                EmitBlock(statement.Else, elseAssigned);
                _assigned.Clear();
                _assigned.UnionWith(thenAssigned);
                _assigned.IntersectWith(elseAssigned);
            }
            else
            {
                _assigned.Clear();
                _assigned.UnionWith(assignedBefore);
            }

            var end = _code.Count;
            _code[jumpEndIndex] = ComputeInstruction.Jump(end);
        }

        private void EmitBlock(IReadOnlyList<ComputeStatement> statements, HashSet<string> assigned)
        {
            var saved = _assigned.ToHashSet(StringComparer.Ordinal);
            _assigned.Clear();
            _assigned.UnionWith(assigned);
            foreach (var statement in statements)
            {
                EmitStatement(statement);
            }

            assigned.Clear();
            assigned.UnionWith(_assigned);
            _assigned.Clear();
            _assigned.UnionWith(saved);
        }

        private void EmitLoop(ComputeLoopStatement statement)
        {
            if (_bindings.ContainsKey(statement.Variable))
            {
                Error(ComputeCodes.BindingDuplicate,
                    $"循环变量 '{statement.Variable}' 与本单元已有绑定重名（禁止遮蔽）", statement.Line);
                return;
            }

            // 求值各一次：先 from 再 to（栈序 = 求值序，Store 逐次弹栈）。
            var fromType = EmitExpression(statement.From);
            if (fromType is null) return;
            var fromSlot = _nextSlot++;
            Emit(ComputeInstruction.Store(fromSlot), statement.Line);

            var toType = EmitExpression(statement.To);
            if (toType is null) return;
            var toSlot = _nextSlot++;
            Emit(ComputeInstruction.Store(toSlot), statement.Line);

            if (fromType.Kind != ComputeTypeKind.Int || toType.Kind != ComputeTypeKind.Int)
            {
                Error(ComputeCodes.TypeMismatch, "loop 的 from/to 必须是 int", statement.Line);
                return;
            }

            var loopSlot = _nextSlot++;
            Emit(ComputeInstruction.Load(fromSlot), statement.Line);
            Emit(ComputeInstruction.Store(loopSlot), statement.Line);

            var checkIndex = _code.Count;
            Emit(ComputeInstruction.Load(loopSlot), statement.Line);
            Emit(ComputeInstruction.Load(toSlot), statement.Line);
            Emit(ComputeInstruction.Compare("lt"), statement.Line);
            var exitIndex = Emit(ComputeInstruction.JumpIfFalse(0), statement.Line);

            _bindings[statement.Variable] = new Binding(loopSlot, ComputeType.Int, false);
            var bodyAssigned = _assigned.ToHashSet(StringComparer.Ordinal);
            bodyAssigned.Add(statement.Variable);
            EmitBlock(statement.Body, bodyAssigned);
            _bindings.Remove(statement.Variable);
            _assigned.Remove(statement.Variable);

            Emit(ComputeInstruction.Load(loopSlot), statement.Line);
            Emit(ComputeInstruction.PushInt(statement.Step), statement.Line);
            Emit(ComputeInstruction.Binary("add"), statement.Line);
            Emit(ComputeInstruction.Store(loopSlot), statement.Line);
            Emit(ComputeInstruction.Jump(checkIndex), statement.Line);
            var end = _code.Count;
            _code[exitIndex] = ComputeInstruction.JumpIfFalse(end);

            var fromLiteral = LiteralInt(statement.From);
            var toLiteral = LiteralInt(statement.To);
            long? iterations = null;
            if (fromLiteral.HasValue && toLiteral.HasValue)
            {
                try
                {
                    var span = toLiteral.Value - fromLiteral.Value;
                    iterations = span <= 0 ? 0 : (span + statement.Step - 1) / statement.Step;
                }
                catch (OverflowException)
                {
                    iterations = null;
                }
            }

            _loops.Add(new ComputeLoopMeta(statement.Line, fromLiteral, toLiteral, statement.Step, iterations, "loop"));
        }

        private void EmitWhile(ComputeWhileStatement statement)
        {
            var checkIndex = _code.Count;
            var condition = EmitExpression(statement.Condition);
            if (condition is null) return;
            if (condition.Kind != ComputeTypeKind.Bool)
            {
                Error(ComputeCodes.TypeMismatch, "while 条件必须是 bool", statement.Line);
                return;
            }

            var exitIndex = Emit(ComputeInstruction.JumpIfFalse(0), statement.Line);
            var assignedBefore = _assigned.ToHashSet(StringComparer.Ordinal);
            EmitBlock(statement.Body, assignedBefore);
            Emit(ComputeInstruction.Jump(checkIndex), statement.Line);
            var end = _code.Count;
            _code[exitIndex] = ComputeInstruction.JumpIfFalse(end);
            _loops.Add(new ComputeLoopMeta(statement.Line, null, null, 0, null, "while"));
        }

        private void EmitReturn(ComputeReturnStatement statement)
        {
            switch (_header.Kind)
            {
                case ComputeUnitKind.Proc:
                    if (statement.Value is not null)
                    {
                        Error(ComputeCodes.TypeMismatch, "kind=proc 的 return 不得带值", statement.Line);
                        return;
                    }

                    Emit(ComputeInstruction.ReturnUnit(), statement.Line);
                    return;
                case ComputeUnitKind.Fn when _header.ReturnType.Kind == ComputeTypeKind.Unit
                                              && statement.Value is null:
                    Emit(ComputeInstruction.ReturnUnit(), statement.Line);
                    return;
            }

            if (statement.Value is null)
            {
                Error(ComputeCodes.ProgramNotProducible,
                    $"kind={ComputeProgramCodec.KindText(_header.Kind)} 的 return 必须带值", statement.Line);
                return;
            }

            var type = EmitExpression(statement.Value);
            if (type is null) return;
            if (type != _header.ReturnType)
            {
                Error(ComputeCodes.TypeMismatch,
                    $"return 类型不匹配：声明 {_header.ReturnType.Display}，实际 {type.Display}", statement.Line);
                return;
            }

            Emit(ComputeInstruction.ReturnValue(), statement.Line);
        }

        private void EmitCallStatement(ComputeCallStatement statement)
        {
            if (!EffectBuiltins.Contains(statement.Call.Name))
            {
                Error(ComputeCodes.BoundaryViolation,
                    $"语句位置只允许效果调用（{string.Join('/', EffectBuiltins)}），'{statement.Call.Name}' 不可作为语句",
                    statement.Line);
                return;
            }

            var type = EmitExpression(statement.Call);
            if (type is null) return;
            Emit(ComputeInstruction.Pop(), statement.Line);
        }

        private ComputeType? EmitExpression(ComputeExpression expression)
        {
            switch (expression)
            {
                case ComputeLiteralExpression literal:
                    return EmitLiteral(literal);
                case ComputeIdentifierExpression identifier:
                    return EmitIdentifier(identifier);
                case ComputeKeyReferenceExpression key:
                    return EmitKeyReference(key);
                case ComputeCallExpression call:
                    return EmitCall(call);
                case ComputeUnaryExpression unary:
                    return EmitUnary(unary);
                case ComputeBinaryExpression binary:
                    return EmitBinary(binary);
                case ComputeListExpression list:
                    return EmitList(list);
                case ComputeMapExpression map:
                    return EmitMap(map);
                default:
                    return Error(ComputeCodes.SyntaxInvalid, "未知表达式", expression.Line);
            }
        }

        private ComputeType? EmitLiteral(ComputeLiteralExpression literal)
        {
            switch (literal.Kind)
            {
                case ComputeLiteralKind.Int:
                    if (!long.TryParse(literal.Text, NumberStyles.None, CultureInfo.InvariantCulture,
                            out var intValue))
                        return Error(ComputeCodes.TypeMismatch, $"int 字面量越界 '{literal.Text}'", literal.Line);
                    Emit(ComputeInstruction.PushInt(intValue), literal.Line);
                    return ComputeType.Int;
                case ComputeLiteralKind.Decimal:
                    if (!decimal.TryParse(literal.Text, NumberStyles.Number, CultureInfo.InvariantCulture,
                            out var decimalValue))
                        return Error(ComputeCodes.TypeMismatch, $"decimal 字面量非法 '{literal.Text}'", literal.Line);
                    Emit(ComputeInstruction.PushDecimal(
                        ComputeValue.DecimalText(ComputeHostBoundary.Quantize(decimalValue))), literal.Line);
                    return ComputeType.Decimal;
                case ComputeLiteralKind.Bool:
                    Emit(ComputeInstruction.PushBool(string.Equals(literal.Text, "true",
                        StringComparison.Ordinal)), literal.Line);
                    return ComputeType.Bool;
                default:
                    Emit(ComputeInstruction.PushString(literal.Text), literal.Line);
                    return ComputeType.String;
            }
        }

        private ComputeType? EmitIdentifier(ComputeIdentifierExpression identifier)
        {
            var binding = _bindings.GetValueOrDefault(identifier.Name);
            if (binding is null)
                return Error(ComputeCodes.NameUndeclared, $"标识符 '{identifier.Name}' 未声明", identifier.Line);
            if (!_assigned.Contains(identifier.Name))
                return Error(ComputeCodes.ReadBeforeDefine,
                    $"'{identifier.Name}' 在读取点未被定值（定值分析按最小不动点）", identifier.Line);
            Emit(ComputeInstruction.Load(binding.Slot), identifier.Line);
            return binding.Type;
        }

        private ComputeType? EmitKeyReference(ComputeKeyReferenceExpression key)
        {
            if (!CheckCoordinate(key.SpaceId, key.Coordinate, key.Line)) return null;
            RequireEffect(ComputeEffect.KeyRead, "键读引用 $" + key.SpaceId + "." + key.Coordinate, key.Line);
            EmitKeyRead(key.SpaceId, key.Coordinate, key.Line);
            return ComputeType.Host;
        }

        private void EmitKeyRead(string space, string coordinate, int line)
        {
            Emit(ComputeInstruction.PushString(space), line);
            Emit(ComputeInstruction.PushString(coordinate), line);
            Emit(ComputeInstruction.Builtin("key_read", 2), line);
        }

        private bool CheckCoordinate(string space, string coordinate, int line)
        {
            if (_coordinateExists is null) return true;
            if (_coordinateExists(space, coordinate)) return true;
            Error(ComputeCodes.NameUndeclared,
                $"键引用 ${space}.{coordinate} 的坐标未在 [Space] 中声明", line);
            return false;
        }

        private void RequireEffect(ComputeEffect effect, string target, int line)
        {
            if (_header.Purity == ComputePurityPolicy.Strict)
            {
                Error(ComputeCodes.PurityInvalid,
                    $"pure=strict 禁止效果（{target}）", line);
                return;
            }

            if (!_header.Effects.Contains(effect))
            {
                Error(ComputeCodes.EffectUndeclared,
                    $"效果未声明：{target} 需要 effects 中包含 {ComputeProgramCodec.EffectText(effect)}", line);
            }
        }

        private ComputeType? EmitUnary(ComputeUnaryExpression unary)
        {
            var operand = EmitExpression(unary.Operand);
            if (operand is null) return null;
            if (unary.Operator == "-")
            {
                if (!operand.IsNumeric)
                    return Error(ComputeCodes.TypeMismatch, $"一元 '-' 需要 int/decimal，实际 {operand.Display}",
                        unary.Line);
                Emit(ComputeInstruction.Unary("neg"), unary.Line);
                return operand;
            }

            if (operand.Kind != ComputeTypeKind.Bool)
                return Error(ComputeCodes.TypeMismatch, $"not 需要 bool，实际 {operand.Display}", unary.Line);
            Emit(ComputeInstruction.Unary("not"), unary.Line);
            return ComputeType.Bool;
        }

        private ComputeType? EmitBinary(ComputeBinaryExpression binary)
        {
            if (binary.Operator is "and" or "or")
            {
                return EmitShortCircuit(binary);
            }

            var left = EmitExpression(binary.Left);
            if (left is null) return null;
            var right = EmitExpression(binary.Right);
            if (right is null) return null;

            switch (binary.Operator)
            {
                case "+":
                    if (left.Kind == ComputeTypeKind.Int && right.Kind == ComputeTypeKind.Int)
                    {
                        Emit(ComputeInstruction.Binary("add"), binary.Line);
                        return ComputeType.Int;
                    }

                    if (left.Kind == ComputeTypeKind.Decimal && right.Kind == ComputeTypeKind.Decimal)
                    {
                        Emit(ComputeInstruction.Binary("add"), binary.Line);
                        return ComputeType.Decimal;
                    }

                    if (left.Kind == ComputeTypeKind.String && right.Kind == ComputeTypeKind.String)
                    {
                        Emit(ComputeInstruction.Binary("add"), binary.Line);
                        return ComputeType.String;
                    }

                    return Error(ComputeCodes.TypeMismatch,
                        $"'+' 两侧类型不兼容：{left.Display} + {right.Display}（无隐式提升）", binary.Line);
                case "-":
                case "*":
                case "/":
                    if (left.Kind == right.Kind && left.IsNumeric)
                    {
                        Emit(ComputeInstruction.Binary(binary.Operator switch
                        {
                            "-" => "sub",
                            "*" => "mul",
                            _ => "div"
                        }), binary.Line);
                        return left;
                    }

                    return Error(ComputeCodes.TypeMismatch,
                        $"'{binary.Operator}' 需要同类型 int/decimal：{left.Display} {binary.Operator} {right.Display}",
                        binary.Line);
                case "%":
                    if (left.Kind == ComputeTypeKind.Int && right.Kind == ComputeTypeKind.Int)
                    {
                        Emit(ComputeInstruction.Binary("mod"), binary.Line);
                        return ComputeType.Int;
                    }

                    return Error(ComputeCodes.TypeMismatch,
                        $"'%' 只接受 int（decimal 请用 mod 内建）：{left.Display} % {right.Display}",
                        binary.Line);
                case "==":
                case "!=":
                    if (!CanCompareEquality(left, right))
                        return Error(ComputeCodes.TypeMismatch,
                            $"'{binary.Operator}' 两侧类型不兼容：{left.Display}/{right.Display}", binary.Line);
                    Emit(ComputeInstruction.Compare(binary.Operator == "==" ? "eq" : "ne"), binary.Line);
                    return ComputeType.Bool;
                case "<":
                case "<=":
                case ">":
                case ">=":
                    if (!CanOrder(left, right))
                        return Error(ComputeCodes.TypeMismatch,
                            $"'{binary.Operator}' 需要同类型 int/decimal/string：{left.Display}/{right.Display}",
                            binary.Line);
                    Emit(ComputeInstruction.Compare(binary.Operator switch
                    {
                        "<" => "lt",
                        "<=" => "le",
                        ">" => "gt",
                        _ => "ge"
                    }), binary.Line);
                    return ComputeType.Bool;
                default:
                    return Error(ComputeCodes.SyntaxInvalid, $"未知运算符 '{binary.Operator}'", binary.Line);
            }
        }

        private ComputeType? EmitShortCircuit(ComputeBinaryExpression binary)
        {
            var left = EmitExpression(binary.Left);
            if (left is null) return null;
            if (left.Kind != ComputeTypeKind.Bool)
                return Error(ComputeCodes.TypeMismatch,
                    $"{binary.Operator} 需要 bool，实际 {left.Display}", binary.Line);

            if (binary.Operator == "and")
            {
                var jumpFalse = Emit(ComputeInstruction.JumpIfFalse(0), binary.Line);
                var right = EmitExpression(binary.Right);
                if (right is null) return null;
                if (right.Kind != ComputeTypeKind.Bool)
                    return Error(ComputeCodes.TypeMismatch,
                        $"and 右侧需要 bool，实际 {right.Display}", binary.Line);
                var jumpEnd = Emit(ComputeInstruction.Jump(0), binary.Line);
                _code[jumpFalse] = ComputeInstruction.JumpIfFalse(_code.Count);
                Emit(ComputeInstruction.PushBool(false), binary.Line);
                _code[jumpEnd] = ComputeInstruction.Jump(_code.Count);
                return ComputeType.Bool;
            }

            var jumpFalseOr = Emit(ComputeInstruction.JumpIfFalse(0), binary.Line);
            Emit(ComputeInstruction.PushBool(true), binary.Line);
            var jumpOver = Emit(ComputeInstruction.Jump(0), binary.Line);
            _code[jumpFalseOr] = ComputeInstruction.JumpIfFalse(_code.Count);
            var rightOr = EmitExpression(binary.Right);
            if (rightOr is null) return null;
            if (rightOr.Kind != ComputeTypeKind.Bool)
                return Error(ComputeCodes.TypeMismatch,
                    $"or 右侧需要 bool，实际 {rightOr.Display}", binary.Line);
            _code[jumpOver] = ComputeInstruction.Jump(_code.Count);
            return ComputeType.Bool;
        }

        private static bool CanCompareEquality(ComputeType left, ComputeType right)
            => left == right && left.Kind is ComputeTypeKind.Int or ComputeTypeKind.Decimal
                or ComputeTypeKind.Bool or ComputeTypeKind.String;

        private static bool CanOrder(ComputeType left, ComputeType right)
            => left == right && left.Kind is ComputeTypeKind.Int or ComputeTypeKind.Decimal
                or ComputeTypeKind.String;

        private ComputeType? EmitList(ComputeListExpression list)
        {
            if (list.Items.Count == 0)
                return Error(ComputeCodes.TypeMismatch, "空列表字面量无法推断元素类型", list.Line);
            var element = EmitExpression(list.Items[0]);
            if (element is null) return null;
            for (var i = 1; i < list.Items.Count; i++)
            {
                var next = EmitExpression(list.Items[i]);
                if (next is null) return null;
                if (next != element)
                {
                    return Error(ComputeCodes.TypeMismatch,
                        $"列表元素类型不一致：{element.Display} 与 {next.Display}", list.Line);
                }
            }

            Emit(ComputeInstruction.NewList(list.Items.Count), list.Line);
            return ComputeType.ListOf(element);
        }

        private ComputeType? EmitMap(ComputeMapExpression map)
        {
            if (map.Entries.Count == 0)
                return Error(ComputeCodes.TypeMismatch, "空 map 字面量无法推断值类型", map.Line);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var valueType = EmitExpression(map.Entries[0].Value);
            if (valueType is null) return null;
            foreach (var entry in map.Entries)
            {
                if (!seen.Add(entry.Key))
                    return Error(ComputeCodes.BindingDuplicate, $"map 字面量键 '{entry.Key}' 重复", map.Line);
            }

            for (var i = 1; i < map.Entries.Count; i++)
            {
                Emit(ComputeInstruction.PushString(map.Entries[i].Key), map.Line);
                var next = EmitExpression(map.Entries[i].Value);
                if (next is null) return null;
                if (next != valueType)
                    return Error(ComputeCodes.TypeMismatch,
                        $"map 值类型不一致：{valueType.Display} 与 {next.Display}", map.Line);
            }

            Emit(ComputeInstruction.NewMap(map.Entries.Count), map.Line);
            return ComputeType.MapOf(valueType);
        }

        private ComputeType? EmitCall(ComputeCallExpression call)
        {
            switch (call.Name)
            {
                case "len":
                    return EmitLen(call);
                case "get":
                    return EmitGet(call);
                case "append":
                    return EmitAppend(call);
                case "put":
                    return EmitPut(call);
                case "has":
                    return EmitHas(call);
                case "mod":
                    return EmitNumericBinaryBuiltin(call, "mod");
                case "abs":
                    return EmitAbs(call);
                case "min":
                case "max":
                    return EmitNumericBinaryBuiltin(call, call.Name);
                case "to_int":
                    return EmitToInt(call);
                case "to_decimal":
                    return EmitToDecimal(call);
                case "str":
                    return EmitStr(call);
                case "parse_int":
                    return EmitParse(call, "parse_int", ComputeType.Int);
                case "parse_decimal":
                    return EmitParse(call, "parse_decimal", ComputeType.Decimal);
                case "to_host":
                    return EmitToHost(call);
                case "from_matrix":
                    return EmitFromMatrix(call);
                case "read_key":
                    return EmitReadKey(call);
                case "key_read":
                    return Error(ComputeCodes.BoundaryViolation,
                        "key_read 是内部指令；请直接使用 $Space.coord 或 read_key($Space.coord)", call.Line);
                case "key_write":
                    return Error(ComputeCodes.BoundaryViolation,
                        "key_write 是内部指令；请使用 write_key($Space.coord, 值)", call.Line);
                case "emit":
                    return EmitEmit(call);
                case "clock_now":
                    return EmitClockNow(call);
                case "rand_int":
                    return EmitRandInt(call);
            }

            var args = new List<ComputeType>();
            foreach (var argument in call.Arguments)
            {
                var type = EmitExpression(argument);
                if (type is null) return null;
                args.Add(type);
            }

            return EmitUnitCall(call, args);
        }

        private ComputeType? EmitUnitCall(ComputeCallExpression call, IReadOnlyList<ComputeType> argTypes)
        {
            var callee = _headers.GetValueOrDefault(call.Name);
            if (callee is null)
                return Error(ComputeCodes.CalleeInvalid,
                    $"调用 '{call.Name}' 不是已声明的 [Compute] 单元", call.Line);
            if (argTypes.Count != callee.Arguments.Count)
                return Error(ComputeCodes.CalleeInvalid,
                    $"调用 '{call.Name}' 参数个数不符：期望 {callee.Arguments.Count}，实际 {argTypes.Count}",
                    call.Line);
            for (var i = 0; i < argTypes.Count; i++)
            {
                if (argTypes[i] != callee.Arguments[i].Type)
                    return Error(ComputeCodes.CalleeInvalid,
                        $"调用 '{call.Name}' 第 {i + 1} 个参数类型不符：期望 {callee.Arguments[i].Type.Display}，实际 {argTypes[i].Display}",
                        call.Line);
            }

            if (_header.Purity == ComputePurityPolicy.Strict
                && (callee.Purity != ComputePurityPolicy.Strict || callee.Effects.Count > 0))
            {
                return Error(ComputeCodes.PurityInvalid,
                    $"pure=strict 的单元不能调用非 strict 的单元 '{call.Name}'", call.Line);
            }

            if (_header.Purity == ComputePurityPolicy.Scoped
                && (callee.Purity == ComputePurityPolicy.Impure
                    || callee.Effects.Contains(ComputeEffect.Clock)
                    || callee.Effects.Contains(ComputeEffect.Random)))
            {
                return Error(ComputeCodes.PurityInvalid,
                    $"pure=scoped 的单元不能调用 impure/含 clock|random 的单元 '{call.Name}'", call.Line);
            }

            _callees.Add(call.Name);
            Emit(ComputeInstruction.Call(call.Name, argTypes.Count), call.Line);
            return callee.Kind == ComputeUnitKind.Proc ? ComputeType.Unit : callee.ReturnType;
        }

        private ComputeType? EmitLen(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            var arg = EmitExpression(call.Arguments[0]);
            if (arg is null) return null;
            if (arg.Kind is not (ComputeTypeKind.String or ComputeTypeKind.List or ComputeTypeKind.Map))
                return Error(ComputeCodes.TypeMismatch, $"len 不支持 {arg.Display}", call.Line);
            Emit(ComputeInstruction.Builtin("len", 1), call.Line);
            return ComputeType.Int;
        }

        private ComputeType? EmitGet(ComputeCallExpression call)
        {
            var argc = call.Arguments.Count;
            if (argc is not (2 or 3))
                return Error(ComputeCodes.CalleeInvalid, "get 需要 2 或 3 个参数", call.Line);
            var container = EmitExpression(call.Arguments[0]);
            if (container is null) return null;
            ComputeType? element = null;
            if (container.Kind == ComputeTypeKind.List)
            {
                var index = EmitExpression(call.Arguments[1]);
                if (index is null) return null;
                if (index.Kind != ComputeTypeKind.Int)
                    return Error(ComputeCodes.TypeMismatch, $"list get 的下标必须是 int，实际 {index.Display}",
                        call.Line);
                element = container.Element;
            }
            else if (container.Kind == ComputeTypeKind.Map)
            {
                var key = EmitExpression(call.Arguments[1]);
                if (key is null) return null;
                if (key.Kind != ComputeTypeKind.String)
                    return Error(ComputeCodes.TypeMismatch, $"map get 的键必须是 string，实际 {key.Display}",
                        call.Line);
                element = container.Element;
            }
            else
            {
                return Error(ComputeCodes.TypeMismatch, $"get 不支持 {container.Display}", call.Line);
            }

            if (argc == 3)
            {
                var fallback = EmitExpression(call.Arguments[2]);
                if (fallback is null) return null;
                if (fallback != element)
                    return Error(ComputeCodes.TypeMismatch,
                        $"get 默认值类型不符：期望 {(element ?? ComputeType.Unit).Display}，实际 {fallback.Display}",
                        call.Line);
            }

            Emit(ComputeInstruction.Builtin("get", argc), call.Line);
            return element;
        }

        private ComputeType? EmitAppend(ComputeCallExpression call)
        {
            if (!CheckArity(call, 2)) return null;
            var list = EmitExpression(call.Arguments[0]);
            if (list is null) return null;
            if (list.Kind != ComputeTypeKind.List)
                return Error(ComputeCodes.TypeMismatch, $"append 第一个参数必须是 list，实际 {list.Display}",
                    call.Line);
            var value = EmitExpression(call.Arguments[1]);
            if (value is null) return null;
            if (value != list.Element)
                return Error(ComputeCodes.TypeMismatch,
                    $"append 元素类型不符：期望 {list.Element?.Display}，实际 {value.Display}", call.Line);
            Emit(ComputeInstruction.Builtin("append", 2), call.Line);
            return list;
        }

        private ComputeType? EmitPut(ComputeCallExpression call)
        {
            if (!CheckArity(call, 3)) return null;
            var map = EmitExpression(call.Arguments[0]);
            if (map is null) return null;
            if (map.Kind != ComputeTypeKind.Map)
                return Error(ComputeCodes.TypeMismatch, $"put 第一个参数必须是 map，实际 {map.Display}",
                    call.Line);
            var key = EmitExpression(call.Arguments[1]);
            if (key is null) return null;
            if (key.Kind != ComputeTypeKind.String)
                return Error(ComputeCodes.TypeMismatch, $"put 键必须是 string，实际 {key.Display}", call.Line);
            var value = EmitExpression(call.Arguments[2]);
            if (value is null) return null;
            if (value != map.Element)
                return Error(ComputeCodes.TypeMismatch,
                    $"put 值类型不符：期望 {map.Element?.Display}，实际 {value.Display}", call.Line);
            Emit(ComputeInstruction.Builtin("put", 3), call.Line);
            return map;
        }

        private ComputeType? EmitHas(ComputeCallExpression call)
        {
            if (!CheckArity(call, 2)) return null;
            var map = EmitExpression(call.Arguments[0]);
            if (map is null) return null;
            if (map.Kind != ComputeTypeKind.Map)
                return Error(ComputeCodes.TypeMismatch, $"has 第一个参数必须是 map，实际 {map.Display}",
                    call.Line);
            var key = EmitExpression(call.Arguments[1]);
            if (key is null) return null;
            if (key.Kind != ComputeTypeKind.String)
                return Error(ComputeCodes.TypeMismatch, $"has 键必须是 string，实际 {key.Display}", call.Line);
            Emit(ComputeInstruction.Builtin("has", 2), call.Line);
            return ComputeType.Bool;
        }

        private ComputeType? EmitNumericBinaryBuiltin(ComputeCallExpression call, string name)
        {
            if (!CheckArity(call, 2)) return null;
            var left = EmitExpression(call.Arguments[0]);
            if (left is null) return null;
            var right = EmitExpression(call.Arguments[1]);
            if (right is null) return null;
            if (!(left.Kind == right.Kind && left.IsNumeric))
                return Error(ComputeCodes.TypeMismatch,
                    $"{name} 需要同类型 int/decimal：{left.Display}/{right.Display}", call.Line);
            Emit(ComputeInstruction.Builtin(name, 2), call.Line);
            return left;
        }

        private ComputeType? EmitAbs(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            var value = EmitExpression(call.Arguments[0]);
            if (value is null) return null;
            if (!value.IsNumeric)
                return Error(ComputeCodes.TypeMismatch, $"abs 需要 int/decimal，实际 {value.Display}", call.Line);
            Emit(ComputeInstruction.Builtin("abs", 1), call.Line);
            return value;
        }

        private ComputeType? EmitToInt(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            var value = EmitExpression(call.Arguments[0]);
            if (value is null) return null;
            switch (value.Kind)
            {
                case ComputeTypeKind.Decimal:
                    Emit(ComputeInstruction.Builtin("to_int", 1), call.Line);
                    return ComputeType.Int;
                case ComputeTypeKind.Host:
                    Emit(ComputeInstruction.Convert("host", "int", "host_to_int_exact"), call.Line);
                    return ComputeType.Int;
                default:
                    return Error(ComputeCodes.TypeMismatch,
                        $"to_int 需要 decimal 或显式 host，实际 {value.Display}", call.Line);
            }
        }

        private ComputeType? EmitToDecimal(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            var value = EmitExpression(call.Arguments[0]);
            if (value is null) return null;
            switch (value.Kind)
            {
                case ComputeTypeKind.Int:
                    Emit(ComputeInstruction.Builtin("to_decimal", 1), call.Line);
                    return ComputeType.Decimal;
                case ComputeTypeKind.Host:
                    Emit(ComputeInstruction.Convert("host", "decimal", "host_to_decimal_exact"), call.Line);
                    return ComputeType.Decimal;
                default:
                    return Error(ComputeCodes.TypeMismatch,
                        $"to_decimal 需要 int 或显式 host，实际 {value.Display}", call.Line);
            }
        }

        private ComputeType? EmitStr(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            var value = EmitExpression(call.Arguments[0]);
            if (value is null) return null;
            if (value.Kind == ComputeTypeKind.Host)
            {
                Emit(ComputeInstruction.Convert("host", "string", "canonical_text"), call.Line);
                return ComputeType.String;
            }

            if (value.Kind is ComputeTypeKind.List or ComputeTypeKind.Map or ComputeTypeKind.Unit)
                return Error(ComputeCodes.TypeMismatch, $"str 不支持 {value.Display}（无隐式字符串化）",
                    call.Line);
            Emit(ComputeInstruction.Builtin("str", 1), call.Line);
            return ComputeType.String;
        }

        private ComputeType? EmitParse(ComputeCallExpression call, string name, ComputeType result)
        {
            if (!CheckArity(call, 1)) return null;
            var value = EmitExpression(call.Arguments[0]);
            if (value is null) return null;
            if (value.Kind != ComputeTypeKind.String)
                return Error(ComputeCodes.TypeMismatch, $"{name} 需要 string，实际 {value.Display}", call.Line);
            Emit(ComputeInstruction.Builtin(name, 1), call.Line);
            return result;
        }

        private ComputeType? EmitToHost(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            var value = EmitExpression(call.Arguments[0]);
            if (value is null) return null;
            if (value.Kind is not (ComputeTypeKind.Int or ComputeTypeKind.Decimal
                or ComputeTypeKind.Bool or ComputeTypeKind.String))
                return Error(ComputeCodes.TypeMismatch, $"to_host 不支持 {value.Display}", call.Line);
            Emit(ComputeInstruction.Convert(value.Display, "host", "canonical_host"), call.Line);
            return ComputeType.Host;
        }

        private ComputeType? EmitFromMatrix(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            var value = EmitExpression(call.Arguments[0]);
            if (value is null) return null;
            if (value.Kind != ComputeTypeKind.Host)
                return Error(ComputeCodes.TypeMismatch,
                    $"from_matrix 需要显式 host 值（宿主 double 必须先经 ComputeHostBoundary），实际 {value.Display}",
                    call.Line);
            Emit(ComputeInstruction.Convert("host", "decimal", "half_even",
                ComputeHostBoundary.DecimalScale), call.Line);
            return ComputeType.Decimal;
        }

        private ComputeType? EmitReadKey(ComputeCallExpression call)
        {
            if (!CheckArity(call, 1)) return null;
            if (call.Arguments[0] is not ComputeKeyReferenceExpression key)
                return Error(ComputeCodes.TypeMismatch, "read_key 的参数必须是 $Space.coord", call.Line);
            if (!CheckCoordinate(key.SpaceId, key.Coordinate, call.Line)) return null;
            RequireEffect(ComputeEffect.KeyRead, "read_key", call.Line);
            EmitKeyRead(key.SpaceId, key.Coordinate, call.Line);
            return ComputeType.Host;
        }

        private ComputeType? EmitEmit(ComputeCallExpression call)
        {
            if (!CheckArity(call, 2)) return null;
            var channel = EmitExpression(call.Arguments[0]);
            if (channel is null) return null;
            if (channel.Kind != ComputeTypeKind.String)
                return Error(ComputeCodes.TypeMismatch, $"emit 通道必须是 string，实际 {channel.Display}",
                    call.Line);
            var value = EmitExpression(call.Arguments[1]);
            if (value is null) return null;
            if (value.Kind != ComputeTypeKind.Host)
                return Error(ComputeCodes.TypeMismatch,
                    $"emit 负载必须是显式 host 值（用 to_host），实际 {value.Display}", call.Line);
            RequireEffect(ComputeEffect.Emit, "emit", call.Line);
            Emit(ComputeInstruction.Builtin("emit", 2), call.Line);
            return ComputeType.Unit;
        }

        private ComputeType? EmitClockNow(ComputeCallExpression call)
        {
            if (!CheckArity(call, 0)) return null;
            RequireEffect(ComputeEffect.Clock, "clock_now", call.Line);
            Emit(ComputeInstruction.Builtin("clock_now", 0), call.Line);
            return ComputeType.Int;
        }

        private ComputeType? EmitRandInt(ComputeCallExpression call)
        {
            if (!CheckArity(call, 2)) return null;
            var lo = EmitExpression(call.Arguments[0]);
            if (lo is null) return null;
            var hi = EmitExpression(call.Arguments[1]);
            if (hi is null) return null;
            if (lo.Kind != ComputeTypeKind.Int || hi.Kind != ComputeTypeKind.Int)
                return Error(ComputeCodes.TypeMismatch, "rand_int 的 lo/hi 必须是 int", call.Line);
            RequireEffect(ComputeEffect.Random, "rand_int", call.Line);
            Emit(ComputeInstruction.Builtin("rand_int", 2), call.Line);
            return ComputeType.Int;
        }

        private bool CheckArity(ComputeCallExpression call, int expected)
        {
            if (call.Arguments.Count == expected) return true;
            Error(ComputeCodes.CalleeInvalid,
                $"{call.Name} 需要 {expected} 个参数，实际 {call.Arguments.Count}", call.Line);
            return false;
        }

        private int Emit(ComputeInstruction instruction, int line)
        {
            _code.Add(instruction);
            _sourceMap.Add(new ComputeSourceMapEntry(_code.Count - 1, line));
            return _code.Count - 1;
        }

        private ComputeType? Error(string code, string message, int line)
        {
            _diagnostics.Add(new ComputeDiagnostic(code, message, _header.SourceFile, line));
            _failed = true;
            return null;
        }

        private static long? LiteralInt(ComputeExpression expression)
            => expression is ComputeLiteralExpression { Kind: ComputeLiteralKind.Int } literal
               && long.TryParse(literal.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        private static bool AllPathsReturn(IReadOnlyList<ComputeStatement> statements)
        {
            if (statements.Count == 0) return false;
            return statements[^1] switch
            {
                ComputeReturnStatement => true,
                ComputeIfStatement branch => branch.Else is not null
                                       && AllPathsReturn(branch.Then)
                                       && AllPathsReturn(branch.Else),
                _ => false
            };
        }
    }
}
