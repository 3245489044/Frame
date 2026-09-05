
namespace TestInfrastructure
{

    public abstract class MockBase
    {
        private readonly List<CallRecord> _callHistory = new();

        public string Name { get; }

        public string? Description { get; set; }

        public int CallCount => _callHistory.Count;

        public bool WasCalled => _callHistory.Count > 0;

        public IReadOnlyList<CallRecord> CallHistory => _callHistory.AsReadOnly();

        protected MockBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description;
        }

        protected CallRecord RecordCall(params object?[] args)
        {
            var record = new CallRecord
            {
                Timestamp = DateTime.UtcNow,
                CallIndex = _callHistory.Count,
                Arguments = args
            };
            _callHistory.Add(record);
            return record;
        }

        public void ResetCallHistory()
        {
            _callHistory.Clear();
        }

        public object?[]? GetCallArgs(int index = 0)
        {
            return index < _callHistory.Count ? _callHistory[index].Arguments : null;
        }

        public T? GetCallArg<T>(int index = 0, int argIndex = 0)
        {
            var args = GetCallArgs(index);
            if (args != null && argIndex < args.Length && args[argIndex] is T t)
                return t;
            return default;
        }

        public override string ToString() => $"{GetType().Name}[{Name}] (calls: {CallCount})";
    }

    public class CallRecord
    {
        public DateTime Timestamp { get; set; }

        public int CallIndex { get; set; }

        public object?[] Arguments { get; set; } = Array.Empty<object?>();
    }


    public static class MockFactory
    {
        public static CallbackRecorder CreateCallback()
        {
            return new CallbackRecorder();
        }

        public static CallbackRecorder CreateCallback(out Action action)
        {
            var recorder = new CallbackRecorder();
            action = () => recorder.Invoke();
            return recorder;
        }

        public static CallbackRecorder CreateCallback(out Func<Task> asyncAction)
        {
            var recorder = new CallbackRecorder();
            asyncAction = async () =>
            {
                recorder.Invoke();
                await Task.CompletedTask;
            };
            return recorder;
        }

        public static FuncMock<TResult> CreateFuncMock<TResult>(TResult returnValue)
        {
            return new FuncMock<TResult>(returnValue);
        }

        public static FuncMock<T, TResult> CreateFuncMock<T, TResult>(Func<T, TResult> func)
        {
            return new FuncMock<T, TResult>(func);
        }

        public static ValueProvider<T> CreateValueProvider<T>(T value)
        {
            return new ValueProvider<T>(value);
        }
    }

    public class CallbackRecorder
    {
        private int _callCount;
        private readonly List<object?[]> _callArgs = new();

        public int CallCount => _callCount;

        public bool WasCalled => _callCount > 0;

        public IReadOnlyList<object?[]> CallArgs => _callArgs.AsReadOnly();

        public void Invoke()
        {
            Interlocked.Increment(ref _callCount);
            _callArgs.Add(Array.Empty<object?>());
        }

        public void Invoke(params object?[] args)
        {
            Interlocked.Increment(ref _callCount);
            _callArgs.Add(args);
        }

        public void Reset()
        {
            _callCount = 0;
            _callArgs.Clear();
        }

        public T? GetArg<T>(int callIndex = 0, int argIndex = 0)
        {
            if (callIndex < _callArgs.Count && argIndex < _callArgs[callIndex].Length)
                return (T?)_callArgs[callIndex][argIndex];
            return default;
        }
    }

    public class FuncMock<TResult>
    {
        private readonly TResult _returnValue;
        private int _callCount;

        public FuncMock(TResult returnValue)
        {
            _returnValue = returnValue;
        }

        public int CallCount => _callCount;
        public bool WasCalled => _callCount > 0;

        public TResult Execute()
        {
            Interlocked.Increment(ref _callCount);
            return _returnValue;
        }

        public Task<TResult> ExecuteAsync()
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_returnValue);
        }
    }

    public class FuncMock<T, TResult>
    {
        private readonly Func<T, TResult> _func;
        private int _callCount;
        private readonly List<T> _callInputs = new();

        public FuncMock(Func<T, TResult> func)
        {
            _func = func;
        }

        public int CallCount => _callCount;
        public bool WasCalled => _callCount > 0;
        public IReadOnlyList<T> CallInputs => _callInputs.AsReadOnly();

        public TResult Execute(T input)
        {
            Interlocked.Increment(ref _callCount);
            _callInputs.Add(input);
            return _func(input);
        }

        public async Task<TResult> ExecuteAsync(T input)
        {
            Interlocked.Increment(ref _callCount);
            _callInputs.Add(input);
            return await Task.FromResult(_func(input));
        }
    }

    public class ValueProvider<T>
    {
        public T Value { get; set; }

        public ValueProvider(T value)
        {
            Value = value;
        }

        public T GetValue() => Value;
        public Task<T> GetValueAsync() => Task.FromResult(Value);
    }
}
