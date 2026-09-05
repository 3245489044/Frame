
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace TConfig
{
    #region Interfaces

    public interface IFunctionUnit
    {
        string Name { get; }

        string Description { get; }

        Type InputType { get; }

        Type OutputType { get; }

        bool IsAsync { get; }

        Task<ExecutionResult> ExecuteAsync(object? input, ExecutionContext? context = null);
    }

    public interface IFunctionUnit<TInput, TOutput> : IFunctionUnit
    {
        Task<ExecutionResult<TOutput>> ExecuteAsync(TInput? input, ExecutionContext? context = null);

        Task<TOutput> ExecuteDirectAsync(TInput input, ExecutionContext? context = null);
    }

    #endregion

    #region Base Class (Non-generic)

    public abstract class FunctionUnitBase : IFunctionUnit
    {
        public string Name { get; protected set; } = string.Empty;

        public string Description { get; protected set; } = string.Empty;

        public Type InputType { get; protected set; } = typeof(object);

        public Type OutputType { get; protected set; } = typeof(object);

        public bool IsAsync { get; protected set; }

        public async Task<ExecutionResult> ExecuteAsync(object? input, ExecutionContext? context = null)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var result = await ExecuteCoreAsync(input, context);
                sw.Stop();
                result.ExecutionTime = sw.Elapsed;
                return result;
            }
            catch (OperationCanceledException)
            {
                return ExecutionResult.Cancelled();
            }
            catch (Exception ex)
            {
                return ExecutionResult.Failure(ex.Message, ex);
            }
        }

        protected abstract Task<ExecutionResult> ExecuteCoreAsync(object? input, ExecutionContext? context);
    }

    #endregion

    #region Generic Base Classes

    internal static class FunctionUnitHelper
    {
        public static bool DefaultCanExecute<TInput>(object input)
        {
            if (input == null)
                return !typeof(TInput).IsValueType || Nullable.GetUnderlyingType(typeof(TInput)) != null;
            return input is TInput;
        }
    }

    public abstract class FunctionUnitBaseCore<TInput, TOutput> : IFunctionUnit<TInput, TOutput>
    {
        public abstract string Name { get; }
        public virtual string Description =>
            GetType().GetCustomAttribute<DescriptionAttribute>()?.Description ?? Name;
        public Type InputType => typeof(TInput);
        public Type OutputType => typeof(TOutput);
        public abstract bool IsAsync { get; }

        public virtual bool CanExecute(object input) => FunctionUnitHelper.DefaultCanExecute<TInput>(input);

        public abstract Task<TOutput> ExecuteDirectAsync(TInput input, ExecutionContext? context = null);

        public abstract Task<ExecutionResult<TOutput>> ExecuteAsync(TInput? input, ExecutionContext? context = null);

        protected abstract Task<TOutput> ExecuteCoreAsync(TInput input, ExecutionContext? context);

        async Task<ExecutionResult> IFunctionUnit.ExecuteAsync(object? input, ExecutionContext? context)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (!CanExecute(input!))
                {
                    return ExecutionResult.Failure(
                        $"Input type mismatch. Expected {InputType.Name}, got {input?.GetType().Name ?? "null"}");
                }

                var typedInput = input == null ? default : (TInput)input;
                var result = await ExecuteCoreAsync(typedInput!, context);

                stopwatch.Stop();
                return ExecutionResult.Succeed(result, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return ExecutionResult.Failure(ex.Message, ex);
            }
        }
    }

    public abstract class FunctionUnitBase<TInput, TOutput> : FunctionUnitBaseCore<TInput, TOutput>
    {
        public override bool IsAsync => false;

        public abstract TOutput Execute(TInput input, ExecutionContext? context = null);

        protected override Task<TOutput> ExecuteCoreAsync(TInput input, ExecutionContext? context)
        {
            return Task.FromResult(Execute(input, context));
        }

        public override Task<TOutput> ExecuteDirectAsync(TInput input, ExecutionContext? context = null)
        {
            if (context?.CancellationToken.IsCancellationRequested == true)
                return Task.FromCanceled<TOutput>(context.CancellationToken);
            return Task.FromResult(Execute(input, context));
        }

        public override Task<ExecutionResult<TOutput>> ExecuteAsync(TInput? input, ExecutionContext? context = null)
        {
            if (context?.CancellationToken.IsCancellationRequested == true)
                return Task.FromResult(ExecutionResult<TOutput>.Cancelled());

            try
            {
                var result = Execute(input!, context);
                return Task.FromResult(ExecutionResult<TOutput>.Ok(result));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ExecutionResult<TOutput>.Fail(ex.Message, ex));
            }
        }
    }

    public abstract class AsyncFunctionUnitBase<TInput, TOutput> : FunctionUnitBaseCore<TInput, TOutput>
    {
        public override bool IsAsync => true;

        public abstract override Task<TOutput> ExecuteDirectAsync(TInput input, ExecutionContext? context = null);

        public abstract override Task<ExecutionResult<TOutput>> ExecuteAsync(TInput? input, ExecutionContext? context = null);

        protected override Task<TOutput> ExecuteCoreAsync(TInput input, ExecutionContext? context)
        {
            return ExecuteDirectAsync(input, context);
        }
    }

    #endregion

    #region Delegate Adapters

    public static class FunctionUnitFactory
    {
        public static IFunctionUnit<TInput, TOutput> Create<TInput, TOutput>(
            Func<TInput, TOutput> function, string? name = null, string? description = null)
            => new DelegateFunctionUnit<TInput, TOutput>(function, name, description);

        public static IFunctionUnit<TInput, TOutput> CreateAsync<TInput, TOutput>(
            Func<TInput, Task<TOutput>> function, string? name = null, string? description = null)
            => new AsyncDelegateFunctionUnit<TInput, TOutput>(function, name, description);
    }

    public class DelegateFunctionUnit<TInput, TOutput> : FunctionUnitBase<TInput, TOutput>
    {
        private readonly Func<TInput, TOutput> _function;
        private readonly string _name;
        private readonly string _description;

        public DelegateFunctionUnit(Func<TInput, TOutput> function, string? name = null, string? description = null)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _name = name ?? function.Method.Name;
            _description = description ?? _name;
        }

        public override string Name => _name;
        public override string Description => _description;

        public override TOutput Execute(TInput input, ExecutionContext? context = null) => _function(input);
    }

    public class AsyncDelegateFunctionUnit<TInput, TOutput> : AsyncFunctionUnitBase<TInput, TOutput>
    {
        private readonly Func<TInput, Task<TOutput>> _function;
        private readonly string _name;
        private readonly string _description;

        public AsyncDelegateFunctionUnit(Func<TInput, Task<TOutput>> function, string? name = null, string? description = null)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _name = name ?? function.Method.Name;
            _description = description ?? _name;
        }

        public override string Name => _name;
        public override string Description => _description;

        public override Task<TOutput> ExecuteDirectAsync(TInput input, ExecutionContext? context = null)
            => _function(input);

        public override Task<ExecutionResult<TOutput>> ExecuteAsync(TInput? input, ExecutionContext? context = null)
        {
            if (input == null)
                return Task.FromResult(ExecutionResult<TOutput>.Fail("Input cannot be null"));

            try
            {
                return ExecuteAsyncCore(input, context);
            }
            catch (Exception ex)
            {
                return Task.FromResult(ExecutionResult<TOutput>.Fail(ex.Message, ex));
            }
        }

        private async Task<ExecutionResult<TOutput>> ExecuteAsyncCore(TInput input, ExecutionContext? context)
        {
            try
            {
                var result = await _function(input);
                return ExecutionResult<TOutput>.Ok(result);
            }
            catch (Exception ex)
            {
                return ExecutionResult<TOutput>.Fail(ex.Message, ex);
            }
        }
    }

    #endregion
}
