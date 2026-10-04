
using System.Reflection;
using System.Text.Json;
using Modules.Registry.AutoRegister.Wrappers;
using TConfig;

namespace Modules.Registry.AutoRegister;

public class AutoRegisterEngine
{
    private readonly AutoRegisterService _service;
    private readonly Func<string, Assembly>? _resolveAssembly;
    private readonly Func<string, string>? _resolveSource;

    public AutoRegisterEngine()
        : this(new AutoRegisterService())
    {
    }

    public AutoRegisterEngine(
        AutoRegisterService service,
        Func<string, Assembly>? resolveAssembly = null,
        Func<string, string>? resolveSource = null)
    {
        // 必填端口（T）：自动注册服务由装配点显式提供；无参构造是组件自装配的唯一入口。
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _resolveAssembly = resolveAssembly;
        _resolveSource = resolveSource;
    }

    public List<(RegistrationEntry Entry, AutoRegisterResult Result)> ProcessConfig(
        string configPath,
        ITrConfigRegistry registry)
    {
        if (!File.Exists(configPath))
        {

            return new List<(RegistrationEntry, AutoRegisterResult)>();
        }

        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<AutoRegisterConfig>(json, options);
        if (config?.Registrations == null || config.Registrations.Count == 0)
        {

            return new List<(RegistrationEntry, AutoRegisterResult)>();
        }

        // 调用方未注入 resolveAssembly 时，统一用"配置目录 → 运行目录 → 注入权威目录 → ALC → 当前目录"的
        // 通用 locator，取代 ProcessBatch 内散落的 CWD Path.GetFullPath + LoadFrom 重复实现（收敛"就近猜"）。
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
        Func<string, Assembly>? resolver = _resolveAssembly;
        if (resolver is null)
        {
            resolver = AssemblySourceLocator.CreateForDirectory(configDir).Resolve;
        }

        var batchResults = _service.ProcessBatch(config, configDir, resolver, _resolveSource);

        foreach (var (entry, result) in batchResults)
        {
            RegisterResultToRegistry(entry, result, registry);
        }


        return batchResults;
    }

    public Dictionary<string, List<(RegistrationEntry Entry, AutoRegisterResult Result)>> ProcessConfigDirectory(
        string configDir,
        ITrConfigRegistry registry,
        string searchPattern = "*-register-config.json")
    {
        var allResults = new Dictionary<string, List<(RegistrationEntry Entry, AutoRegisterResult Result)>>();

        if (!Directory.Exists(configDir))
        {

            return allResults;
        }

        var configFiles = Directory.GetFiles(configDir, searchPattern);
        foreach (var configFile in configFiles)
        {
            var fileName = Path.GetFileName(configFile);

            var results = ProcessConfig(configFile, registry);
            allResults[fileName] = results;
        }

        return allResults;
    }

        private static void RegisterResultToRegistry(
            RegistrationEntry entry,
            AutoRegisterResult result,
            ITrConfigRegistry registry)
        {
            var wrapperType = entry.Wrapper?.Type ?? "passthrough";
            var wrapper = DelegateWrappers.GetByName(wrapperType);

            int successCount = 0;
            foreach (var item in result.RegisteredItems)
            {
                if (item.Status != RegisteredItemStatus.Success || item.Executor == null)
                    continue;

                try
                {
                    var wrappedDelegate = wrapper.Wrap(item);

                    if (item.Parameters is { Length: > 0 })
                    {
                        var paramConfigs = item.Parameters.Select(p => new ParameterDescriptor
                        {
                            Name = p.Name,
                            Type = p.Type,
                            IsOptional = p.IsOptional,
                            DefaultValue = p.DefaultValue
                        }).ToArray();

                        // 关键：真实参数描述符必须随委托注册，否则注册表按委托签名
                        //（passthrough 为 Func<object[],Task<object>>）推断 desc.Parameters 为单个 object[]，
                        // 导致 Func_Execute 等消费方参数类型丢失、批量执行/脚本测试失败。
                        registry.Register(item.RegisteredName, wrappedDelegate, paramConfigs);
                    }
                    else
                    {
                        registry.Register(item.RegisteredName, wrappedDelegate);
                    }

                    successCount++;
                }
                catch (Exception)
                {
                    throw;
                }
            }

        }
}
