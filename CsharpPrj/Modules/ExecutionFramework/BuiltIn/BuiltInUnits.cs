
using EXContext = TConfig.ExecutionContext;
using Modules.Transformers;

namespace ExecutionFramework.BuiltIn;

public class NoOpUnit : TConfig.FunctionUnitBase<object?, object?>

{
    public override string Name => "NoOp";

    public override object? Execute(object? input, EXContext? context = null) => input;
}

public class DelayUnit : TConfig.FunctionUnitBase<int, int>

{
    public override string Name => "Delay";

    public override int Execute(int input, EXContext? context = null)
    {
        Thread.Sleep(input);
        return input;
    }
}

public class LogUnit : TConfig.FunctionUnitBase<object?, object?>
{
    public override string Name => "Log";

    public override object? Execute(object? input, EXContext? context = null)
        => input;
}

public class TransformUnit<TInput, TOutput> : TConfig.FunctionUnitBase<TInput, TOutput>
{
    private readonly Modules.Transformers.ITransformer<TInput, TOutput> _transformer;

    public TransformUnit(Func<TInput, TOutput> transform, string? name = null)
    {
        if (transform == null) throw new ArgumentNullException(nameof(transform));
        _transformer = new Func<TInput, TOutput>(transform)
            .ToTransformer(name ?? "Transform");
    }

    public TransformUnit(Modules.Transformers.ITransformer<TInput, TOutput> transformer, string? name = null)
    {
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    }

    public override string Name => _transformer.Name;

    public override TOutput Execute(TInput input, EXContext? context = null)
        => _transformer.Transform(input);
}

public class ConditionalUnit<TInput> : TConfig.FunctionUnitBase<TInput, TInput>
{
    private readonly Modules.Transformers.ITransformer<TInput, TInput> _transformer;
    private readonly string _name;

    public ConditionalUnit(Func<TInput, bool> condition, string? name = null)
    {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        _name = name ?? "Conditional";
        _transformer = new Modules.Transformers.ConditionalTransformer<TInput, TInput>(
            _name,
            condition,
            new Func<TInput, TInput>(input => input).ToTransformer("PassThrough"),
            null);
    }

    public override string Name => _name;

    public override TInput Execute(TInput input, EXContext? context = null)
        => _transformer.Transform(input);
}
