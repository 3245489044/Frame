
using System.Reflection;

namespace TConfig
{
    public interface IParameterDescriptor
    {
        string Name { get; }

        Type Type { get; }

        bool IsOptional { get; }

        object? DefaultValue { get; }
    }

    public interface IFunctionDescriptor
    {
        string Name { get; }

        string Description { get; }

        Type ReturnType { get; }

        bool IsAsync { get; }

        IReadOnlyList<IParameterDescriptor> Parameters { get; }
    }

    public class ParameterDescriptor : IParameterDescriptor, IParamConfig
    {
        public string Name { get; set; } = string.Empty;

        public Type Type { get; set; } = typeof(object);

        public bool IsOptional { get; set; }

        public object? DefaultValue { get; set; }

        public static ParameterDescriptor FromParameterInfo(ParameterInfo parameter)
        {
            return new ParameterDescriptor
            {
                Name = parameter.Name ?? "param",
                Type = parameter.ParameterType,
                IsOptional = parameter.IsOptional,
                DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null
            };
        }
    }

    public class FunctionDescriptor : IFunctionDescriptor, ITrConfig
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public Type ReturnType { get; set; } = typeof(void);

        public bool IsAsync { get; set; }

        public IReadOnlyList<IParameterDescriptor> Parameters { get; set; } = Array.Empty<IParameterDescriptor>();

        public Delegate? Delegate { get; set; }


        Type ITrConfig.InputType => typeof(object);

        Type ITrConfig.OutputType => ReturnType;

        IReadOnlyList<IParamConfig> ITrConfig.Parameters =>
            Parameters.Select(p => new ParameterDescriptor
            {
                Name = p.Name,
                Type = p.Type,
                IsOptional = p.IsOptional,
                DefaultValue = p.DefaultValue
            }).ToArray();

        object? ITrConfig.Invoke(params object?[] args)
        {
            if (Delegate != null)
            {
                return Delegate.DynamicInvoke(args);
            }
            throw new InvalidOperationException($"Function '{Name}' has no associated delegate.");
        }

        public static FunctionDescriptor FromContract(Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
        {
            ArgumentNullException.ThrowIfNull(function);
            ArgumentNullException.ThrowIfNull(parameters);

            var method = function.Method;
            var paramDescriptors = parameters
                .Where(p => p.Type != typeof(CancellationToken))
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name,
                    Type = p.Type,
                    IsOptional = p.IsOptional,
                    DefaultValue = p.DefaultValue
                })
                .ToArray();

            return new FunctionDescriptor
            {
                Name = method.Name,
                Description = description ?? string.Empty,
                ReturnType = method.ReturnType,
                IsAsync = TrConfig.IsAsyncType(method.ReturnType),
                Parameters = paramDescriptors,
                Delegate = function
            };
        }

        public static FunctionDescriptor FromDelegate(Delegate function, string? description = null)
        {
            ArgumentNullException.ThrowIfNull(function);

            var method = function.Method;
            var parameters = method.GetParameters();

            if (parameters.Length == 0 && function.GetType().GetMethod("Invoke") is MethodInfo invokeMethod)
            {
                method = invokeMethod;
                parameters = invokeMethod.GetParameters();
            }

            var paramDescriptors = parameters
                .Where(p => p.ParameterType != typeof(CancellationToken))
                .Select(ParameterDescriptor.FromParameterInfo)
                .ToArray();

            return new FunctionDescriptor
            {
                Name = method.Name,
                Description = description ?? string.Empty,
                ReturnType = method.ReturnType,
                IsAsync = TrConfig.IsAsyncType(method.ReturnType),
                Parameters = paramDescriptors,
                Delegate = function
            };
        }
    }
}
