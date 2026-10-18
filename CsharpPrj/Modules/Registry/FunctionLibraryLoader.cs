
using System.Reflection;
using System.Text.Json;
using PluginHostContract;
using TConfig;

namespace Modules.Registry;

    public static class FunctionLibraryLoader
    {
        /// <summary>域函数库清单文件名约定（每域自产，随构建部署于其扩展清单 deps.json 同目录）；
        /// 宿主按扩展清单目录合并 —— 宿主只认目录+文件名约定，不认域内类型，扩展域内重构不波及宿主。
        /// 权威: 函数库注册 IoC 解耦（宿主 config 仅留宿主域）。</summary>
        public const string DomainManifestFileName = "function-libraries.domain.json";

        private static readonly HashSet<string> _loadedLibraryTypes = new(StringComparer.Ordinal);

        /// <summary>反射调用失败时解包 <see cref="TargetInvocationException"/>，把库内真实异常（含堆栈）抛给调用方，
        /// 避免只留下 "Target of an invocation" 这类无法定位根因的外层黑盒。</summary>
        private static string DescribeInvocationError(Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            var text = inner.Message;
            if (inner.StackTrace != null)
                text += "\n" + inner.StackTrace;
            return text;
        }

        public sealed class LoadEntryResult
        {
            public string Name { get; init; } = "";

            public bool Success { get; init; }

            public int FunctionCount { get; init; }

            public string? Error { get; init; }

            public bool Required { get; init; }

            public string? ManifestPath { get; init; }

            public string? ConfigPath { get; init; }

            public string? AssemblyPath { get; init; }

            public IReadOnlyList<string> SearchedCandidates { get; init; } = Array.Empty<string>();
        }

        public static IReadOnlyList<LoadEntryResult> Load(
            FunctionRegistry registry,
            string? configPath = null,
            bool autoDiscover = false,
            IEnumerable<string>? domainManifestPaths = null,
            FunctionLibraryEnvironment? environment = null)
        {
            ArgumentNullException.ThrowIfNull(registry);

            var results = new List<LoadEntryResult>();

            var nameTracker = new DuplicateNameTracker();

            if (configPath != null)
            {
                LoadFromConfig(registry, configPath, nameTracker, results, environment, defaultRequired: true);
            }

            // 域清单合并（方案A IoC）：各扩展域自产清单经宿主发现后按同一装载路径注册；
            // 缺失（域未部署/未构建）静默跳过，不阻断宿主（灰态降级约定）。
            if (domainManifestPaths != null)
            {
                foreach (var manifestPath in domainManifestPaths)
                {
                    if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                        continue;
                    LoadFromConfig(registry, manifestPath, nameTracker, results, environment, defaultRequired: false);
                }
            }

            if (autoDiscover)
            {
                LoadFromAutoDiscovery(registry, nameTracker, results);
            }

            nameTracker.AssertNoDuplicates();

            return results.AsReadOnly();
        }

        /// <summary>
        /// P2：把域函数库清单装载到**插件级函数表**（不触碰全局 FunctionRegistry，不按类型名排斥换代重注册）。
        /// 每代由 PluginHost 传入独立 table；旧 table 随旧 ALC 弃引用。
        /// </summary>
        public static IReadOnlyList<LoadEntryResult> LoadInto(
            PluginFunctionTable table,
            string configPath,
            Func<string, Type?>? resolveType = null,
            FunctionLibraryEnvironment? environment = null)
        {
            ArgumentNullException.ThrowIfNull(table);
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"函数库配置文件不存在: {configPath}", configPath);

            var manifestPath = Path.GetFullPath(configPath);
            var results = new List<LoadEntryResult>();
            var registry = new FunctionRegistry();
            var nameTracker = new DuplicateNameTracker();
            var generationGuard = new HashSet<string>(StringComparer.Ordinal);

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("libraries", out var libraries))
                return results;

            foreach (var lib in libraries.EnumerateArray())
            {
                var descriptor = ParseDescriptor(lib, defaultRequired: false);
                if (!descriptor.Enabled || string.IsNullOrWhiteSpace(descriptor.Name) || string.IsNullOrWhiteSpace(descriptor.TypeName))
                    continue;

                IFunctionLibraryContext? context = null;
                if (environment != null)
                    context = environment.CreateContext(descriptor, manifestPath);

                try
                {
                    var type = (resolveType ?? ResolveTypeFromQualifiedName)(descriptor.TypeName);
                    if (type == null)
                    {
                        results.Add(new LoadEntryResult
                        {
                            Name = descriptor.Name,
                            Success = false,
                            Required = descriptor.Required,
                            ManifestPath = manifestPath,
                            ConfigPath = context?.ResolveOptionalResource(descriptor.ConfigPath),
                            Error = $"无法加载类型 '{descriptor.TypeName}'"
                        });
                        continue;
                    }

                    RegisterType(registry, type, descriptor.MethodName, nameTracker, generationGuard, context);
                    results.Add(new LoadEntryResult
                    {
                        Name = descriptor.Name,
                        Success = true,
                        Required = descriptor.Required,
                        ManifestPath = manifestPath,
                        ConfigPath = context?.ResolveOptionalResource(descriptor.ConfigPath)
                    });
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    results.Add(new LoadEntryResult
                    {
                        Name = descriptor.Name,
                        Success = false,
                        Required = descriptor.Required,
                        ManifestPath = manifestPath,
                        ConfigPath = context?.ResolveOptionalResource(descriptor.ConfigPath),
                        Error = DescribeInvocationError(ex)
                    });
                }
            }

            nameTracker.AssertNoDuplicates();

            foreach (var name in registry.GetRegisteredNames())
            {
                if (registry.TryGetDelegate(name, out var handler) && handler is not null)
                    table.Set(name, handler);
            }

            return results.AsReadOnly();
        }

        /// <summary>
        /// 由扩展层装配清单（各产品 <c>*.deps.json</c>，见 <c>ExtensionGenerationFactory.Current.Manifests</c>）
        /// 所在目录推导域函数库清单路径（存在者，去重）。宿主不认域内类型，只按目录+文件名约定发现。
        /// </summary>
        public static IReadOnlyList<string> DiscoverDomainManifests(IEnumerable<string>? extensionManifestPaths)
        {
            var found = new List<string>();
            if (extensionManifestPaths is null)
                return found;

            foreach (var manifestPath in extensionManifestPaths)
            {
                if (string.IsNullOrWhiteSpace(manifestPath))
                    continue;

                var dir = Path.GetDirectoryName(manifestPath);
                if (string.IsNullOrEmpty(dir))
                    continue;

                var candidate = Path.Combine(dir, DomainManifestFileName);
                if (File.Exists(candidate) && !found.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    found.Add(candidate);
            }

            return found;
        }

        /// <summary>在指定目录内发现域函数库清单（存在者返回绝对路径，否则 null）。供无扩展清单上下文的宿主（如 MCP 宿主）按自身输出目录发现。</summary>
        public static string? FindDomainManifest(string? directory)
        {
            if (string.IsNullOrEmpty(directory))
                return null;

            var candidate = Path.Combine(directory, DomainManifestFileName);
            return File.Exists(candidate) ? candidate : null;
        }


    private static void LoadFromConfig(
        FunctionRegistry registry,
        string configPath,
        DuplicateNameTracker nameTracker,
        List<LoadEntryResult> results,
        FunctionLibraryEnvironment? environment,
        bool defaultRequired)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"函数库配置文件不存在: {configPath}", configPath);

        var manifestPath = Path.GetFullPath(configPath);
        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("libraries", out var libraries))
            return;

        foreach (var lib in libraries.EnumerateArray())
        {
            var descriptor = ParseDescriptor(lib, defaultRequired);
            if (!descriptor.Enabled || string.IsNullOrWhiteSpace(descriptor.Name) || string.IsNullOrWhiteSpace(descriptor.TypeName))
                continue;

            IFunctionLibraryContext? context = null;
            if (environment != null)
                context = environment.CreateContext(descriptor, manifestPath);

            try
            {
                var type = ResolveTypeFromQualifiedName(descriptor.TypeName);
                if (type == null)
                {
                    results.Add(new LoadEntryResult
                    {
                        Name = descriptor.Name,
                        Success = false,
                        Required = descriptor.Required,
                        ManifestPath = manifestPath,
                        ConfigPath = context?.ResolveOptionalResource(descriptor.ConfigPath),
                        Error = $"无法加载类型 '{descriptor.TypeName}'"
                    });
                    continue;
                }

                RegisterType(registry, type, descriptor.MethodName, nameTracker, context: context);
                results.Add(new LoadEntryResult
                {
                    Name = descriptor.Name,
                    Success = true,
                    FunctionCount = 0,
                    Required = descriptor.Required,
                    ManifestPath = manifestPath,
                    ConfigPath = context?.ResolveOptionalResource(descriptor.ConfigPath)
                });
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                results.Add(new LoadEntryResult
                {
                    Name = descriptor.Name,
                    Success = false,
                    Required = descriptor.Required,
                    ManifestPath = manifestPath,
                    ConfigPath = context?.ResolveOptionalResource(descriptor.ConfigPath),
                    Error = DescribeInvocationError(ex)
                });
            }
        }
    }

    private static FunctionLibraryDescriptor ParseDescriptor(JsonElement lib, bool defaultRequired)
    {
        static string? ReadString(JsonElement node, string name)
            => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        static bool ReadBool(JsonElement node, string name, bool fallback)
            => node.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;

        static IReadOnlyList<string> ReadStringArray(JsonElement node, string name)
        {
            if (!node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();
        }

        return new FunctionLibraryDescriptor
        {
            Name = ReadString(lib, "name") ?? string.Empty,
            TypeName = ReadString(lib, "type") ?? string.Empty,
            MethodName = ReadString(lib, "method"),
            Enabled = ReadBool(lib, "enabled", true),
            Required = ReadBool(lib, "required", defaultRequired),
            ResourceRoot = ReadString(lib, "resourceRoot"),
            ConfigPath = ReadString(lib, "config"),
            ExpectedPath = ReadString(lib, "expected"),
            Assemblies = ReadStringArray(lib, "assemblies"),
            AssemblySearchRoots = ReadStringArray(lib, "assemblySearchRoots")
        };
    }

    /// <summary>
    /// 按程序集限定名解析函数库类型。
    /// 两段式：① 默认 ALC（deps.json 已登记的正常库，如 FileDriven.Business/FrontEndDriven）用 Type.GetType；
    /// ② 默认 ALC 失败时交扩展代数工厂（当前代数的 scoped ALC）显式发现（扩展层 Agent.* / GameEditor.*）。
    /// 不再依赖全局 AssemblyLoadContext.Default.Resolving 隐式兜底。
    /// </summary>
    private static Type? ResolveTypeFromQualifiedName(string typeStr)
    {
        var type = Type.GetType(typeStr, throwOnError: false);
        if (type != null)
            return type;

        return ExtensionGenerationFactory.ResolveType(typeStr);
    }

    private static void LoadFromAutoDiscovery(
        FunctionRegistry registry,
        DuplicateNameTracker nameTracker,
        List<LoadEntryResult> results)
    {
        var libraryTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => !asm.IsDynamic && !asm.ReflectionOnly)
            .SelectMany(asm =>
            {
                try { return asm.GetExportedTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => t is { IsClass: true, IsAbstract: true, IsPublic: true }
                        && t.GetCustomAttribute<FunctionLibraryAttribute>() != null)
            .ToList();

        foreach (var type in libraryTypes)
        {
            var attr = type.GetCustomAttribute<FunctionLibraryAttribute>()!;
            if (!attr.AutoDiscover)
                continue;

            var typeKey = type.FullName ?? type.Name;
            lock (_loadedLibraryTypes)
            {
                if (_loadedLibraryTypes.Contains(typeKey))
                    continue;
            }

            try
            {
                var countBefore = registry.GetRegisteredNames().Count();
                RegisterType(registry, type, methodName: null, nameTracker);
                var registeredCount = registry.GetRegisteredNames().Count() - countBefore;

                results.Add(new LoadEntryResult
                {
                    Name = type.Name,
                    Success = true,
                    FunctionCount = registeredCount
                });
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                results.Add(new LoadEntryResult
                {
                    Name = type.Name,
                    Success = false,
                    Error = DescribeInvocationError(ex)
                });
            }
        }
    }

    private static void RegisterType(
        FunctionRegistry registry,
        Type type,
        string? methodName,
        DuplicateNameTracker nameTracker,
        HashSet<string>? generationGuard = null,
        IFunctionLibraryContext? context = null)
    {
        var typeKey = type.FullName ?? type.Name;
        var guard = generationGuard ?? _loadedLibraryTypes;
        lock (guard)
        {
            if (!guard.Add(typeKey))
            {
                throw new InvalidOperationException(
                    $"函数库类型 '{typeKey}' 已被注册一次。每个函数库只允许通过 FunctionLibraryLoader 注册一次，" +
                    "请检查 function-libraries.json 中是否存在重复条目，以及是否存在其他代码路径重复调用了 FunctionLibraryLoader.Load()。");
            }
        }

        MethodInfo? method = null;

        if (!string.IsNullOrEmpty(methodName))
        {
            method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        }

        if (method == null)
        {
            method = type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(ITrConfigRegistry), typeof(IFunctionLibraryContext) });
        }

        if (method == null)
        {
            method = type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(ITrConfigRegistry) });
        }

        if (method == null)
        {
            method = type.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        }

        if (method == null)
        {
            throw new InvalidOperationException(
                $"类型 '{type.FullName}' 没有可用的注册方法。需要 RegisterAll(ITrConfigRegistry, IFunctionLibraryContext)、" +
                "RegisterAll(ITrConfigRegistry) 或 Register()。");
        }

        var parameters = method.GetParameters();
        if (parameters.Length == 2
            && typeof(ITrConfigRegistry).IsAssignableFrom(parameters[0].ParameterType)
            && typeof(IFunctionLibraryContext).IsAssignableFrom(parameters[1].ParameterType))
        {
            if (context == null)
            {
                throw new InvalidOperationException(
                    $"'{type.FullName}.{method.Name}' 需要 IFunctionLibraryContext，但当前调用未提供 FunctionLibraryEnvironment。");
            }

            var wrappingRegistry = new ConflictDetectingRegistry(registry, nameTracker);
            method.Invoke(null, new object[] { wrappingRegistry, context });
        }
        else if (parameters.Length == 1 && typeof(ITrConfigRegistry).IsAssignableFrom(parameters[0].ParameterType))
        {
            var wrappingRegistry = new ConflictDetectingRegistry(registry, nameTracker);
            method.Invoke(null, new object[] { wrappingRegistry });
        }
        else if (parameters.Length == 0)
        {
            method.Invoke(null, null);
        }
        else
        {
            throw new InvalidOperationException(
                $"'{type.FullName}.{method.Name}' 的签名不兼容。支持 RegisterAll(ITrConfigRegistry, IFunctionLibraryContext)、" +
                "RegisterAll(ITrConfigRegistry) 或 Register()。");
        }
    }


    private sealed class DuplicateNameTracker
    {
        private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Name, string Source1, string Source2)> _collisions = new();

        public bool TryRegister(string name, out string? existingSource)
        {
            if (_registered.Contains(name))
            {
                existingSource = "先前已注册";
                _collisions.Add((name, "先前已注册", "本次注册"));
                return false;
            }

            _registered.Add(name);
            existingSource = null;
            return true;
        }

        public void AssertNoDuplicates()
        {
            if (_collisions.Count == 0)
                return;

            var details = string.Join("; ", _collisions.Select(c =>
                $"'{c.Name}'"));
            throw new InvalidOperationException(
                $"检测到重复注册的函数名: [{details}]。每个函数名只允许注册一次。" +
                "请检查 function-libraries.json 以及各个 Library 的 RegisterAll 方法，确保没有重复注册。");
        }
    }


    private sealed class ConflictDetectingRegistry : ITrConfigRegistry
    {
        private readonly FunctionRegistry _inner;
        private readonly DuplicateNameTracker _tracker;

        public ConflictDetectingRegistry(FunctionRegistry inner, DuplicateNameTracker tracker)
        {
            _inner = inner;
            _tracker = tracker;
        }

        public void Register(string name, Delegate function, string? description = null)
        {
            if (!_tracker.TryRegister(name, out _))
            {
                throw new InvalidOperationException(
                    $"函数名 '{name}' 被重复注册。每个函数名在 FunctionRegistry 中必须唯一。");
            }
            _inner.Register(name, function, description);
        }

        public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
        {
            if (!_tracker.TryRegister(name, out _))
            {
                throw new InvalidOperationException(
                    $"函数名 '{name}' 被重复注册。每个函数名在 FunctionRegistry 中必须唯一。");
            }
            _inner.Register(name, function, parameters, description);
        }


        public void Register(string name, ITrConfig config)
            => _inner.Register(name, config);

        public void Unregister(string name)
            => _inner.Unregister(name);

        public bool TryGetConfig(string name, out ITrConfig config)
            => _inner.TryGetConfig(name, out config);

        public bool Contains(string name)
            => _inner.Contains(name);

        public IEnumerable<string> GetRegisteredNames()
            => _inner.GetRegisteredNames();

        public void Clear()
            => _inner.Clear();

        public Delegate? Find(string functionName)
            => _inner.Find(functionName);

        public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor)
            => _inner.TryGetDescriptor(name, out descriptor);

        public bool TryGetDelegate(string name, out Delegate? function)
            => _inner.TryGetDelegate(name, out function);
    }
}