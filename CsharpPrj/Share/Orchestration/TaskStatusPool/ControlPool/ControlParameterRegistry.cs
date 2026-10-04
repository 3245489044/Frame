
#nullable enable
using Share.TaskStatusPool.Abstractions;
using Share.TaskStatusPool.Core;
using Share.TaskStatusPool.Morphisms;

namespace Share.TaskStatusPool.ControlPool
{
    public class ControlParameterRegistry : PoolRegistry<string, IPoolContext>
    {
        public ControlParameterRegistry()
            : base("ControlParameterRegistry", StringComparer.OrdinalIgnoreCase)
        {
        }


        public void RegisterMapping(string controlName, string paramName, string functionName, Type targetType)
        {
            PortGuard.Required(controlName, nameof(controlName));
            PortGuard.Required(paramName, nameof(paramName));
            PortGuard.Required(functionName, nameof(functionName));
            PortGuard.Required(targetType, nameof(targetType));

            RegisterOrUpdate(controlName, new ParameterMapMorphism(paramName, functionName, targetType));
        }

        public void RegisterMapping(string controlName, string paramName, string functionName)
        {
            RegisterMapping(controlName, paramName, functionName, typeof(string));
        }

        public void RegisterMappings(IEnumerable<(string ControlName, string ParamName, string FunctionName, Type TargetType)> mappings)
        {
            PortGuard.Required(mappings, nameof(mappings));

            foreach (var (controlName, paramName, functionName, targetType) in mappings)
            {
                RegisterMapping(controlName, paramName, functionName, targetType);
            }
        }


        public ParameterMappingInfo? GetMappingInfo(string controlName)
        {
            if (TryGet(controlName, out var morphism) && morphism is ParameterMapMorphism paramMorphism)
            {
                return new ParameterMappingInfo
                {
                    ControlName = controlName,
                    ParamName = paramMorphism.ParamName,
                    FunctionName = paramMorphism.FunctionName,
                    TargetType = paramMorphism.TargetType
                };
            }
            return null;
        }

        public IEnumerable<ParameterMappingInfo> GetMappingsForFunction(string functionName)
        {
            foreach (var key in Keys)
            {
                var info = GetMappingInfo(key);
                if (info != null && string.Equals(info.FunctionName, functionName, StringComparison.Ordinal))
                {
                    yield return info;
                }
            }
        }

        public override string ToString()
            => $"ControlParameterRegistry[{Name}] Count={Count}";
    }


    public class ParameterMappingInfo
    {
        public string ControlName { get; init; } = string.Empty;

        public string ParamName { get; init; } = string.Empty;

        public string FunctionName { get; init; } = string.Empty;

        public Type TargetType { get; init; } = typeof(string);

        public override string ToString()
            => $"{ControlName} → {FunctionName}({ParamName}:{TargetType.Name})";
    }
}
