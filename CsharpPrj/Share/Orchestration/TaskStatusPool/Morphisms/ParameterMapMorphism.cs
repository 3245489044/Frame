
#nullable enable
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Morphisms
{
    public class ParameterMapMorphism : IOrderedMorphism<IPoolContext, IPoolContext>
    {
        private readonly ControlPool.ControlParameterRegistry _registry;

        public string Name => "ParameterMap";

        public int Order => 1;

        internal string ParamName { get; }

        internal string FunctionName { get; }

        internal Type TargetType { get; }

        public ParameterMapMorphism(ControlPool.ControlParameterRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            ParamName = string.Empty;
            FunctionName = string.Empty;
            TargetType = typeof(string);
        }

        internal ParameterMapMorphism(string paramName, string functionName, Type targetType)
        {
            ParamName = paramName ?? throw new ArgumentNullException(nameof(paramName));
            FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
            TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
            _registry = null!;
        }

        public IPoolContext Morph(IPoolContext input)
        {
            var controlName = input.Get<string>("_controlName");
            if (string.IsNullOrEmpty(controlName))
                return input;

            var controlValue = input.Get<object>("_controlValue");

            if (_registry != null)
            {
                var info = _registry.GetMappingInfo(controlName);
                if (info != null)
                {
                    input.Set("_paramName", info.ParamName);
                    input.Set("_functionName", info.FunctionName);
                    input.Set("_paramValue", ConvertValue(controlValue, info.TargetType));
                }
            }
            else
            {
                input.Set("_paramName", ParamName);
                input.Set("_functionName", FunctionName);
                input.Set("_paramValue", ConvertValue(controlValue, TargetType));
            }

            return input;
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null)
                return null;

            if (targetType.IsInstanceOfType(value))
                return value;

            var str = value.ToString();
            if (str == null)
                return null;

            try
            {
                if (targetType == typeof(string)) return str;
                if (targetType == typeof(int)) return int.Parse(str);
                if (targetType == typeof(long)) return long.Parse(str);
                if (targetType == typeof(double)) return double.Parse(str);
                if (targetType == typeof(decimal)) return decimal.Parse(str);
                if (targetType == typeof(bool)) return bool.Parse(str);
                if (targetType == typeof(DateTime)) return DateTime.Parse(str);
                if (targetType.IsEnum) return Enum.Parse(targetType, str);

                return Convert.ChangeType(str, targetType);
            }
            catch
            {
                return value;
            }
        }
    }
}
