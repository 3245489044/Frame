
using System.Reflection;
using Modules.Registry.AutoRegister.Cache;
using Modules.Registry.AutoRegister.Delegate;
using Modules.Registry.AutoRegister.Description;
using Modules.Registry.AutoRegister.Extractors;
using Modules.Registry.AutoRegister.Filters;
using Modules.Registry.AutoRegister.Naming;
using TConfig;

namespace Modules.Registry.AutoRegister;


public class AutoRegisterConfig
{
    public List<RegistrationEntry> Registrations { get; set; } = new();
}

public class RegistrationEntry
{
    public SourceConfig Source { get; set; } = new();
    public string ClassName { get; set; } = string.Empty;
    public MethodFilterConfig? MethodFilters { get; set; }
    public NamingConfig? Naming { get; set; }
    public DescriptionConfig? Description { get; set; }
    
    public Wrappers.WrapperConfig? Wrapper { get; set; }
}

public class SourceConfig
{
    public string Type { get; set; } = "assembly";
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 配置驱动的"权威产物"声明（可选）：当程序集是源码树编译产物时，声明它在
    /// <c>&lt;configDir 上溯到含该产物的源码根&gt;\Sub\Config\</c> 下的原二进制，AutoRegister 据此
    /// <b>原地 LoadFrom 原二进制</b>，而不是要求运行目录持有它（收敛"就近猜/拷贝"）。
    /// 典型：<c>Sub="BuildOutput", Config="Debug"</c> → <c>...\CsharpPrj\BuildOutput\Debug\Mathematics.dll</c>。
    /// </summary>
    public SourceOutputConfig? Output { get; set; }
}

public class SourceOutputConfig
{
    /// <summary>产物根子目录名（相对上溯命中的源码根）。默认 <c>BuildOutput</c>。</summary>
    public string Sub { get; set; } = "BuildOutput";

    /// <summary>构建配置名（产物子目录下的配置目录）。默认 <c>Debug</c>。</summary>
    public string Config { get; set; } = "Debug";
}


public class NamingConfig
{
    public string Convention { get; set; } = "MethodName";
    public string? Separator { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
}

public class DescriptionConfig
{
    public string Source { get; set; } = "none";
    public string? Fallback { get; set; }
}


public class ExtractedMethodInfo
{
    public string MethodName { get; init; } = string.Empty;

    public string ContainingClassName { get; init; } = string.Empty;

    public string? ContainingNestedClassName { get; init; }

    public Type ReturnType { get; init; } = typeof(void);

    public ParameterInfo[] Parameters { get; init; } = Array.Empty<ParameterInfo>();

    public string Description { get; init; } = string.Empty;

    public bool IsStatic { get; init; }

    public bool IsAsync { get; init; }

    public Func<object[], Task<object>>? Executor { get; set; }

    public class ParameterInfo : IParameterDescriptor
    {
        public string Name { get; init; } = string.Empty;
        public Type Type { get; init; } = typeof(object);
        public bool IsOptional { get; init; }
        public object? DefaultValue { get; init; }
    }
}


public enum RegisteredItemStatus
{
    Success,
    Failure,
    Skipped
}

public class RegisteredItemInfo
{
    public string RegisteredName { get; init; } = string.Empty;

    public string MethodName { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Type? ReturnType { get; init; }

    public bool IsAsync { get; init; }

    public ExtractedMethodInfo.ParameterInfo[]? Parameters { get; init; }

    public Func<object[], Task<object>>? Executor { get; init; }

    public RegisteredItemStatus Status { get; init; } = RegisteredItemStatus.Success;

    public string? ErrorMessage { get; init; }
}

public class AutoRegisterResult
{
    private readonly List<RegisteredItemInfo> _items = new();

    public IReadOnlyList<RegisteredItemInfo> RegisteredItems => _items.AsReadOnly();

    public int SuccessCount { get; private set; }

    public int FailCount { get; private set; }

    public int SkippedCount { get; private set; }

    public int TotalCount => _items.Count;

    public void AddItem(string registeredName, ExtractedMethodInfo method, string description)
    {
        _items.Add(new RegisteredItemInfo
        {
            RegisteredName = registeredName,
            MethodName = method.MethodName,
            ClassName = method.ContainingClassName,
            Description = description,
            ReturnType = method.ReturnType,
            IsAsync = method.IsAsync,
            Parameters = method.Parameters,
            Executor = method.Executor,
            Status = RegisteredItemStatus.Success
        });
        SuccessCount++;
    }

    public void AddFailure(string methodName, string errorMessage)
    {
        _items.Add(new RegisteredItemInfo
        {
            RegisteredName = methodName,
            MethodName = methodName,
            Status = RegisteredItemStatus.Failure,
            ErrorMessage = errorMessage
        });
        FailCount++;
    }

    public void AddSkipped(string methodName, string reason)
    {
        _items.Add(new RegisteredItemInfo
        {
            RegisteredName = methodName,
            MethodName = methodName,
            Status = RegisteredItemStatus.Skipped,
            ErrorMessage = reason
        });
        SkippedCount++;
    }
}


public class AutoRegisterService
{
    private readonly IAutoRegisterCache? _cache;

    public AutoRegisterService(IAutoRegisterCache? cache = null)
    {
        _cache = cache;
    }

    public AutoRegisterResult ProcessAssembly(Assembly assembly, RegistrationEntry entry)
    {
        var cacheKey = !string.IsNullOrEmpty(assembly.Location) && _cache != null
            ? CacheKeyGenerator.GenerateForAssembly(assembly.Location, entry)
            : null;

        if (cacheKey != null && _cache!.TryGetManifest(cacheKey, out var cachedMethods))
        {
            RebindExecutors(cachedMethods, assembly);
            return BuildResult(cachedMethods, entry);
        }

        var extractor = new ReflectionExtractor(assembly);
        var methods = extractor.ExtractMethods(entry.ClassName, entry.MethodFilters);

        if (cacheKey != null)
        {
            _cache!.SaveManifest(cacheKey, methods, entry.Source);
        }

        return BuildResult(methods, entry);
    }


    public AutoRegisterResult ProcessSource(string sourceCode, RegistrationEntry entry, string? sourcePath = null)
    {
        return Task.Run(async () =>
            await ProcessSourceInternal(sourceCode, entry, sourcePath, async: true)
                .ConfigureAwait(false))
            .GetAwaiter().GetResult();
    }

    public async Task<AutoRegisterResult> ProcessSourceAsync(string sourceCode, RegistrationEntry entry, string? sourcePath = null)
    {
        return await ProcessSourceInternal(sourceCode, entry, sourcePath, async: true);
    }

    private async Task<AutoRegisterResult> ProcessSourceInternal(string sourceCode, RegistrationEntry entry, string? sourcePath = null, bool async = false)
    {
        var cacheKey = sourcePath != null && _cache != null
            ? CacheKeyGenerator.GenerateForSource(sourcePath, sourceCode, entry)
            : null;

        if (cacheKey != null)
        {
            if (_cache!.TryGetManifest(cacheKey, out var cachedMethods))
            {
                if (_cache.TryGetCompiledAssembly(cacheKey, out var cachedAssembly))
                {
                    RebindExecutors(cachedMethods, cachedAssembly);
                }
                return BuildResult(cachedMethods, entry);
            }
        }

        var roslynExtractor = new RoslynExtractor(sourceCode);
        var methods = roslynExtractor.ExtractMethods(entry.ClassName, entry.MethodFilters);

        if (cacheKey != null)
        {
            _cache!.SaveManifest(cacheKey, methods, entry.Source);

            var compiledAssembly = roslynExtractor.GetCompiledAssembly();
            if (compiledAssembly != null)
            {
                _cache.SaveCompiledAssembly(cacheKey, compiledAssembly);
            }
        }

        return BuildResult(methods, entry);
    }

    public List<(RegistrationEntry Entry, AutoRegisterResult Result)> ProcessBatch(
        AutoRegisterConfig config,
        Func<string, Assembly>? resolveAssembly = null,
        Func<string, string>? resolveSource = null)
        => ProcessBatch(config, configDir: null, resolveAssembly, resolveSource);

    public List<(RegistrationEntry Entry, AutoRegisterResult Result)> ProcessBatch(
        AutoRegisterConfig config,
        string? configDir,
        Func<string, Assembly>? resolveAssembly = null,
        Func<string, string>? resolveSource = null)
    {
        var results = new List<(RegistrationEntry, AutoRegisterResult)>();

        foreach (var entry in config.Registrations)
        {
            try
            {
                AutoRegisterResult result;
                if (entry.Source.Type == "assembly")
                {
                    var assemblyPath = entry.Source.Path;

                    // 配置驱动：source.Output 声明了源码树 BuildOutput\<Config> 产物时，
                    // 从 configDir 上溯命中产物目录并原地 LoadFrom 原二进制（运行目录不持有该 dll）。
                    if (entry.Source.Output != null
                        && !string.IsNullOrWhiteSpace(configDir)
                        && !Path.IsPathRooted(assemblyPath))
                    {
                        var productDir = OutputArtifactLocator.LocateProductDirectory(
                            configDir, entry.Source.Output.Sub, entry.Source.Output.Config, assemblyPath);
                        var productPath = OutputArtifactLocator.ResolveAssemblyPath(productDir, assemblyPath);
                        if (productPath != null && File.Exists(productPath))
                        {
                            var productAsm = Assembly.LoadFrom(productPath);
                            result = ProcessAssembly(productAsm, entry);
                            results.Add((entry, result));
                            continue;
                        }
                    }

                    if (resolveAssembly != null)
                    {

                        var resolved = resolveAssembly(assemblyPath);

                        if (resolved != null)
                        {

                            result = ProcessAssembly(resolved, entry);

                            results.Add((entry, result));
                            continue;
                        }
                    }
                    if (!Path.IsPathRooted(assemblyPath))
                    {
                        assemblyPath = Path.GetFullPath(assemblyPath);
                    }
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    result = ProcessAssembly(assembly, entry);
                }
                else if (entry.Source.Type == "source")
                {
                    var sourcePath = string.IsNullOrEmpty(entry.Source.Path) ? null : entry.Source.Path;
                    if (sourcePath != null && !Path.IsPathRooted(sourcePath))
                    {
                        sourcePath = Path.GetFullPath(sourcePath);
                    }
                    var sourceCode = resolveSource?.Invoke(entry.Source.Path)
                        ?? (sourcePath != null ? File.ReadAllText(sourcePath) : throw new InvalidOperationException("Source path is required when resolveSource is not provided"));
                    result = ProcessSource(sourceCode, entry, sourcePath);
                }

                else
                {
                    result = new AutoRegisterResult();
                    result.AddFailure(entry.ClassName, $"Unknown source type: {entry.Source.Type}");
                }

                results.Add((entry, result));
            }
            catch (Exception ex)
            {

                var errorResult = new AutoRegisterResult();
                errorResult.AddFailure(entry.ClassName, ex.Message);
                results.Add((entry, errorResult));
            }
        }

        return results;
    }

    private static void RebindExecutors(List<ExtractedMethodInfo> methods, Assembly assembly)
    {
        var delegateFactory = new Delegate.DelegateFactory();

        foreach (var method in methods)
        {
            try
            {
                var methodInfo = assembly.FindMethodInfo(method);
                if (methodInfo == null)
                    continue;

                method.Executor = delegateFactory.CreateExecutor(methodInfo);
            }
            catch
            {
            }
        }
    }

    private AutoRegisterResult BuildResult(List<ExtractedMethodInfo> methods, RegistrationEntry entry)
    {
        var result = new AutoRegisterResult();

        var namingConvention = NamingConventions.GetByName(
            entry.Naming?.Convention ?? "MethodName");

        var descriptionProvider = DescriptionProviders.GetByName(
            entry.Description?.Source ?? "none");

        // 注册名去重：重载/同名方法（如 Reduce_Mean 的两个重载、Softmax_Compute 的三个重载）
        // 会生成相同注册名，只注册首个，后续记 Skipped——避免 FunctionRegistry 重复注册崩溃。
        var registeredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var method in methods)
        {
            try
            {
                var registeredName = namingConvention.GenerateName(
                    method.ContainingClassName,
                    method.ContainingNestedClassName,
                    method.MethodName,
                    entry.Naming ?? new NamingConfig());

                if (!registeredNames.Add(registeredName))
                {
                    result.AddSkipped(registeredName,
                        $"重载/同名方法 '{registeredName}' 已注册（保留首个），后续重载跳过");
                    continue;
                }

                var description = descriptionProvider.GetDescription(method);
                if (string.IsNullOrEmpty(description) && entry.Description?.Fallback != null)
                {
                    description = entry.Description.Fallback;
                }

                result.AddItem(registeredName, method, description);
            }
            catch (Exception ex)
            {
                result.AddFailure(method.MethodName, ex.Message);
            }
        }

        return result;
    }
}
