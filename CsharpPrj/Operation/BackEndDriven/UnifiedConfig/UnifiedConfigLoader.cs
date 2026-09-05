
using System.Reflection;
using System.Linq.Expressions;
using System.Text.Json;
using TConfig;

namespace BackEndDriven.UnifiedConfig
{
    public class UnifiedConfigLoader
    {
        private readonly ITrConfigRegistry _registry;
        private readonly JsonSerializerOptions _jsonOptions;

        private static readonly JsonSerializerOptions DefaultOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public UnifiedConfigLoader(ITrConfigRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _jsonOptions = DefaultOptions;
        }

        /// <summary>统一格式探测：JSON 根含 ui 对象（schema/functions/ui 包裹结构）。</summary>
        public static bool IsUnifiedFormat(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("ui", out var ui)
                    && ui.ValueKind == JsonValueKind.Object;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>纯解析入口（不依赖 Registry）：运行时统一配置加载适配器使用。</summary>
        public static UnifiedConfig ParseFromFile(string configPath)
            => ParseFromFileCore(configPath, DefaultOptions);

        public UnifiedConfig LoadFromFile(string configPath)
            => ParseFromFileCore(configPath, _jsonOptions);

        private static UnifiedConfig ParseFromFileCore(string configPath, JsonSerializerOptions options)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"统一配置文件不存在: {configPath}", configPath);

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<UnifiedConfig>(json, options);

            if (config == null)
                throw new InvalidOperationException($"无法解析统一配置文件: {configPath}");

            return config;
        }

        public UnifiedConfigLoadResult LoadAndDispatch(string configPath)
        {
            var config = LoadFromFile(configPath);

            int delegateCount = 0;
            int workflowCount = 0;

            foreach (var func in config.Functions)
            {
                switch (func.Type?.ToLowerInvariant())
                {
                    case "delegate":
                        RegisterDelegate(func);
                        delegateCount++;
                        break;
                    case "workflow":
                        RegisterWorkflow(func);
                        workflowCount++;
                        break;
                }
            }

            return new UnifiedConfigLoadResult
            {
                Config = config,
                FunctionCount = delegateCount,
                WorkflowCount = workflowCount,
                UiConfig = config.Ui
            };
        }

        public int LoadFunctionsOnly(string configPath)
        {
            var config = LoadFromFile(configPath);
            int count = 0;

            foreach (var func in config.Functions)
            {
                switch (func.Type?.ToLowerInvariant())
                {
                    case "delegate":
                        RegisterDelegate(func);
                        count++;
                        break;
                    case "workflow":
                        RegisterWorkflow(func);
                        count++;
                        break;
                }
            }

            return count;
        }


        private void RegisterDelegate(UnifiedRegistrationEntry func)
        {
            var type = Type.GetType(func.Source);
            if (type == null)
                throw new InvalidOperationException($"类型未找到: {func.Source}");

            var method = type.GetMethod(func.Method,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException($"方法未找到: {type.FullName}.{func.Method}");

            var methodParams = method.GetParameters();
            if (func.Parameters != null && func.Parameters.Count > 0)
            {
                for (int i = 0; i < Math.Min(methodParams.Length, func.Parameters.Count); i++)
                {
                    var configParam = func.Parameters[i];
                    var methodParam = methodParams[i];

                    if (!string.IsNullOrEmpty(configParam.Name) &&
                        !string.Equals(configParam.Name, methodParam.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"[Warning] UnifiedConfigLoader: 参数名不匹配 (索引 {i})。"
                            + $"配置名='{configParam.Name}', 方法名='{methodParam.Name}'。"
                            + $"函数: {func.Name}, 方法: {type.FullName}.{func.Method}");
                    }

                    if (!string.IsNullOrEmpty(configParam.Type))
                    {
                        var configType = Type.GetType(configParam.Type);
                        if (configType != null && configType != methodParam.ParameterType)
                        {
                            System.Diagnostics.Trace.WriteLine(
                                $"[Warning] UnifiedConfigLoader: 参数类型不匹配 (索引 {i})。"
                                + $"配置类型='{configParam.Type}', 方法类型='{methodParam.ParameterType.FullName}'。"
                                + $"函数: {func.Name}, 方法: {type.FullName}.{func.Method}");
                        }
                    }
                }
            }

            var delegateType = GetDelegateType(method);
            var dlg = method.CreateDelegate(delegateType);

            var paramConfigs = func.Parameters?.Select(p => new ParamConfig
            {
                Name = p.Name,
                Type = Type.GetType(p.Type) ?? typeof(object),
                IsOptional = p.IsOptional,
                DefaultValue = p.DefaultValue
            }).ToArray() ?? Array.Empty<IParamConfig>();

            _registry.Register(func.Name, dlg, paramConfigs, func.Description);
        }

        private void RegisterWorkflow(UnifiedRegistrationEntry func)
        {
            var trConfig = new TrConfigRecord
            {
                Name = func.Name,
                Description = func.Description ?? string.Empty,
                InputType = typeof(object),
                OutputType = typeof(object),
                IsAsync = false,
                Parameters = Array.Empty<IParamConfig>()
            };

            _registry.Register(func.Name, trConfig);
        }

        private static Type GetDelegateType(MethodInfo method)
        {
            var paramTypes = method.GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

            if (method.ReturnType == typeof(void))
                return Expression.GetActionType(paramTypes);
            else
                return Expression.GetFuncType(paramTypes.Concat(new[] { method.ReturnType }).ToArray());
        }
    }

    public class UnifiedConfigLoadResult
    {
        public UnifiedConfig Config { get; set; } = null!;

        public int FunctionCount { get; set; }

        public int WorkflowCount { get; set; }

        public UnifiedUiConfig? UiConfig { get; set; }

        public int TotalCount => FunctionCount + WorkflowCount;
    }
}
