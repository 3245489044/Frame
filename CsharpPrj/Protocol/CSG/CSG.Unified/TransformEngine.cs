using CSG.Unified;
using CSG.Unified.Validation;

namespace CSG.Unified;

public class TransformEngine : ITransformEngine
{

    private readonly Dictionary<string, TransformOperation> _operations = new();
    private readonly Dictionary<string, Func<object, object>> _operationHandlers = new();

    public TransformEngine()
    {
        RegisterBuiltInOperations();
    }

    private void RegisterBuiltInOperations()
    {
        RegisterOperation(new TransformOperation
        {
            Name = "ConfigToCanonical",
            Label = "配置 → 规范形式",
            Description = "将配置对象转换为统一的规范中间表示",
            Category = TransformCategory.Transform,
            InputType = typeof(IConfiguration),
            OutputType = typeof(CanonicalForm),
            IsReversible = true,
            ReverseOperationName = "CanonicalToConfig",
            Tags = new[] { "config", "canonical", "transform" }
        }, input => ConfigToCanonical((IConfiguration)input));

        RegisterOperation(new TransformOperation
        {
            Name = "CanonicalToConfig",
            Label = "规范形式 → 配置",
            Description = "将规范中间表示转换回配置对象",
            Category = TransformCategory.Transform,
            InputType = typeof(CanonicalForm),
            OutputType = typeof(IConfiguration),
            IsReversible = true,
            ReverseOperationName = "ConfigToCanonical",
            Tags = new[] { "config", "canonical", "transform" }
        }, input => CanonicalToConfig<DefaultConfiguration>((CanonicalForm)input));

        RegisterOperation(new TransformOperation
        {
            Name = "StateMachineToCanonical",
            Label = "状态机 → 规范形式",
            Description = "将状态机转换为统一的规范中间表示",
            Category = TransformCategory.Transform,
            InputType = typeof(IStateMachine),
            OutputType = typeof(CanonicalForm),
            IsReversible = true,
            ReverseOperationName = "CanonicalToStateMachine",
            Tags = new[] { "state-machine", "canonical", "transform" }
        }, input => StateMachineToCanonical((IStateMachine)input));

        RegisterOperation(new TransformOperation
        {
            Name = "CanonicalToStateMachine",
            Label = "规范形式 → 状态机",
            Description = "将规范中间表示转换回状态机",
            Category = TransformCategory.Transform,
            InputType = typeof(CanonicalForm),
            OutputType = typeof(IStateMachine),
            IsReversible = true,
            ReverseOperationName = "StateMachineToCanonical",
            Tags = new[] { "state-machine", "canonical", "transform" }
        }, input => CanonicalToStateMachine((CanonicalForm)input));

        RegisterOperation(new TransformOperation
        {
            Name = "GraphToCanonical",
            Label = "图 → 规范形式",
            Description = "将图结构转换为统一的规范中间表示",
            Category = TransformCategory.Transform,
            InputType = typeof(IGraph),
            OutputType = typeof(CanonicalForm),
            IsReversible = true,
            ReverseOperationName = "CanonicalToGraph",
            Tags = new[] { "graph", "canonical", "transform" }
        }, input => GraphToCanonical((IGraph)input));

        RegisterOperation(new TransformOperation
        {
            Name = "CanonicalToGraph",
            Label = "规范形式 → 图",
            Description = "将规范中间表示转换回图结构",
            Category = TransformCategory.Transform,
            InputType = typeof(CanonicalForm),
            OutputType = typeof(IGraph),
            IsReversible = true,
            ReverseOperationName = "GraphToCanonical",
            Tags = new[] { "graph", "canonical", "transform" }
        }, input => CanonicalToGraph((CanonicalForm)input));

        RegisterOperation(new TransformOperation
        {
            Name = "ValidateConsistency",
            Label = "一致性验证",
            Description = "验证 C/S/G 三层是否一致",
            Category = TransformCategory.Validate,
            InputType = typeof(Tuple<CanonicalForm, CanonicalForm, CanonicalForm>),
            OutputType = typeof(ValidationResult),
            Tags = new[] { "validate", "consistency" }
        }, input => {
            var args = (Tuple<CanonicalForm, CanonicalForm, CanonicalForm>)input;
            return ValidateConsistency(args.Item1, args.Item2, args.Item3);
        });
    }

    public void RegisterOperation(TransformOperation operation, Func<object, object> handler)
    {
        _operations[operation.Name] = operation;
        _operationHandlers[operation.Name] = handler;
    }


    public CanonicalForm ConfigToCanonical(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        CsgProtocol.EnsureCompatible(config.ProtocolVersion, config.ConfigType);
        return config.ToCanonical();
    }

    public TConfig CanonicalToConfig<TConfig>(CanonicalForm form)
        where TConfig : IConfiguration, new()
    {
        ArgumentNullException.ThrowIfNull(form);
        var config = new TConfig();
        if (config is DefaultConfiguration defaultConfig)
        {
            defaultConfig.SetForm(form);
        }
        return config;
    }


    public CanonicalForm StateMachineToCanonical(IStateMachine sm)
    {
        ArgumentNullException.ThrowIfNull(sm);
        return sm.ToCanonical();
    }

    public IStateMachine CanonicalToStateMachine(CanonicalForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        throw new NotSupportedException(
            "CanonicalForm to IStateMachine conversion requires a registered adapter. " +
            "Use AdapterRegistry to register a state machine adapter.");
    }


    public CanonicalForm GraphToCanonical(IGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.ToCanonical();
    }

    public IGraph CanonicalToGraph(CanonicalForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        throw new NotSupportedException(
            "CanonicalForm to IGraph conversion requires a registered adapter. " +
            "Use AdapterRegistry to register a graph adapter.");
    }


    private readonly ConsistencyValidator _consistencyValidator = new();

    public ValidationResult ValidateConsistency(
        CanonicalForm configForm,
        CanonicalForm stateMachineForm,
        CanonicalForm graphForm)
    {
        return _consistencyValidator.Validate(configForm, stateMachineForm, graphForm);
    }


    public List<TransformOperation> ListOperations()
    {
        return _operations.Values.ToList();
    }

    public List<TransformOperation> ListOperationsByCategory(string category)
    {
        var normalized = category.ToLowerInvariant() switch
        {
            "transform" => TransformCategory.Transform,
            "validate" => TransformCategory.Validate,
            "build" => TransformCategory.Build,
            _ => category
        };

        return _operations.Values
            .Where(op => op.Category.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public object CallOperation(string operationName, object input)
    {
        if (!_operations.TryGetValue(operationName, out var operation))
        {
            throw new NotSupportedException(
                $"操作 '{operationName}' 不存在。可用操作: {string.Join(", ", _operations.Keys)}");
        }

        if (!_operationHandlers.TryGetValue(operationName, out var handler))
        {
            throw new NotSupportedException(
                $"操作 '{operationName}' 没有注册处理程序");
        }

        if (input != null && !operation.InputType.IsInstanceOfType(input))
        {
            throw new InvalidOperationException(
                $"操作 '{operationName}' 期望输入类型 '{operation.InputType.Name}', 但收到 '{input.GetType().Name}'");
        }

        return handler(input);
    }
}

