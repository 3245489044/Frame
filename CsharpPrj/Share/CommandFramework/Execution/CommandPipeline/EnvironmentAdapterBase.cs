
using CommandFramework.Abstractions;

namespace CommandFramework.Execution
{
    public abstract class EnvironmentAdapterBase : IEnvironmentAdapter
    {
        public abstract string Name { get; }

        public int DefaultTimeoutMs { get; set; } = 5000;

        public virtual Task<ICommandExecutionResult> DoAsync(string target, string value)
        {
            return Task.FromResult(NotSupported("Do", target));
        }

        public virtual Task<ICommandExecutionResult> GetAsync(string target)
        {
            return Task.FromResult(NotSupported("Get", target));
        }

        public virtual Task<ICommandExecutionResult> LookAsync(string target)
        {
            return Task.FromResult(NotSupported("Look", target));
        }

        public virtual Task<ICommandExecutionResult> WaitAsync(string target, string? condition = null)
        {
            return Task.FromResult(NotSupported("Wait", target));
        }

        public virtual Task<ICommandExecutionResult> AssertAsync(string target, string? expectedValue = null)
        {
            return Task.FromResult(NotSupported("Assert", target));
        }

        public virtual Task<ICommandExecutionResult> DiscoverAsync(string? filter = null)
        {
            return Task.FromResult(NotSupported("Discover", filter ?? "*"));
        }

        private static string BuildCommandString(string operation, string target)
            => $"{operation} {target}";

        protected ICommandExecutionResult NotSupported(string operation, string target)
            => CommandExecutionResult<object>.Failure(
                $"Operation '{operation}' is not supported by {Name} adapter", BuildCommandString(operation, target), "NotSupported");

        protected ICommandExecutionResult Success(string operation, string target, string? value = null)
            => CommandExecutionResult<object>.Success(value, BuildCommandString(operation, target));

        protected ICommandExecutionResult Failure(string operation, string target, string error, string? errorCode = null)
            => CommandExecutionResult<object>.Failure(error, BuildCommandString(operation, target), errorCode);
    }
}
