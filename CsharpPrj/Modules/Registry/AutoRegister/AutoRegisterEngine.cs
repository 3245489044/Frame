
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

    public AutoRegisterEngine(
        AutoRegisterService? service = null,
        Func<string, Assembly>? resolveAssembly = null,
        Func<string, string>? resolveSource = null)
    {
        _service = service ?? new AutoRegisterService();
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

        var batchResults = _service.ProcessBatch(config, _resolveAssembly, _resolveSource);

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
