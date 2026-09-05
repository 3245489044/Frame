using System.Reflection;
using System.Text.Json;
using CallCenter.Core.Tools;
using TConfig;

namespace CallCenter.Core.Discovery;

/// <summary>
/// C#Track 的函数库发现源。以仓库既定的跨程序集函数库约定（静态类 + <c>public static void
/// RegisterAll(ITrConfigRegistry)</c>，与 FileDriven 的 function-libraries.json 一致）为「真实发现源」：
/// 由调用方给定一个或多个库（config 清单 / 显式库类型），反射执行其 <c>RegisterAll</c>，把注册函数投影为
/// <see cref="ToolDescriptor"/>，彻底替代在 <c>Program</c> 中硬编码工具描述。
/// <para>
/// 用一个轻量收集器（<see cref="Collector"/>）实现 <see cref="ITrConfigRegistry"/>，仅捕获注册快照并做同名
/// 冲突检测，不依赖 Modules.Registry 的运行栈 —— 平台侧只消费注册契约，不绑定某套注册表实现。
/// </para>
/// </summary>
public sealed class CSharpFunctionLibrarySource : IToolSource
{
    private readonly IReadOnlyList<string> _libraryTypeNames;
    private ITrConfigRegistry? _lastRegistry;

    /// <summary>
    /// 最近一次 <see cref="DiscoverDefinitions"/> 实际执行 RegisterAll 所用的 <see cref="ITrConfigRegistry"/>
    ///（轻量收集器）。宿主可在其上构建统一执行栈（如 ExecutionFramework.FunctionExecutor），使 CSharpTrack 与
    /// 函数库捕获的注册表同源，等价接线（Phase A）。未发现前为 null。
    /// </summary>
    public ITrConfigRegistry? Registry => _lastRegistry;

    public string Name { get; } = "csharp.function-libraries";

    private CSharpFunctionLibrarySource(IReadOnlyList<string> libraryTypeNames)
        => _libraryTypeNames = libraryTypeNames;

    /// <summary>从 function-libraries.json 形状的清单构建（条目：name/type/enabled）。</summary>
    public static CSharpFunctionLibrarySource FromConfig(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"C# 函数库发现清单不存在: {configPath}", configPath);

        return FromJson(File.ReadAllText(configPath));
    }

    /// <summary>
    /// 从清单 JSON 文本直接构建（C5：watch 先一次读入字节快照，指纹与解析同源该快照，避免对路径二次打开读到不一致内容）。
    /// </summary>
    public static CSharpFunctionLibrarySource FromJson(string json)
        => new(ParseLibraryTypes(json));

    private static IReadOnlyList<string> ParseLibraryTypes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("libraries", out var libraries))
            return Array.Empty<string>();

        var types = new List<string>();
        foreach (var lib in libraries.EnumerateArray())
        {
            var enabled = lib.TryGetProperty("enabled", out var e) ? e.GetBoolean() : true;
            var type = lib.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (enabled && !string.IsNullOrWhiteSpace(type))
                types.Add(type!);
        }

        return types;
    }

    /// <summary>从显式库类型构建（测试 / 程序内组装用，运行时以 config 清单为准）。</summary>
    public static CSharpFunctionLibrarySource FromLibraryTypes(IEnumerable<Type> libraryTypes)
        => new(libraryTypes
            .Select(t => t.AssemblyQualifiedName ?? t.FullName ?? t.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray()!);

    /// <summary>与 <see cref="DiscoverDefinitions"/> 同源发现，仅返回描述符（供 CsgToolCatalog/数据面）。</summary>
    public IReadOnlyList<ToolDescriptor> Discover()
        => DiscoverDefinitions().Select(d => d.Descriptor).ToArray();

    /// <summary>
    /// 一次真实发现：对每个库类型解析并执行其静态 <c>RegisterAll(ITrConfigRegistry)</c>，把注册函数投影为
    /// 工具定义（描述符 + 可执行 <see cref="Delegate"/>）。描述符进数据面，委托供 CSharpTrack Runtime 轨执行，
    /// 二者同源产出，避免运行时重复发现。同名冲突（同一源内重复注册）抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    public IReadOnlyList<CSharpToolDefinition> DiscoverDefinitions()
    {
        var collector = new Collector();
        _lastRegistry = collector;
        var any = false;

        foreach (var typeName in _libraryTypeNames)
        {
            var type = ResolveType(typeName);
            if (type == null)
                throw new InvalidOperationException(
                    $"无法解析 C# 函数库类型 '{typeName}'。请确认清单 type 为 assembly-qualified name，且宿主已引用该库程序集。");

            var registerAll = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "RegisterAll"
                                     && m.GetParameters().Length == 1
                                     && m.GetParameters()[0].ParameterType == typeof(ITrConfigRegistry));

            if (registerAll == null)
                throw new InvalidOperationException(
                    $"函数库类型 '{type.FullName}' 缺少可用的 public static RegisterAll(ITrConfigRegistry)。");

            try
            {
                registerAll.Invoke(null, new object[] { collector });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            {
                // 反射调用把库内异常（如同名冲突）包成 TargetInvocationException → 解包，向调用方抛清晰错误。
                throw ex.InnerException;
            }
            any = true;
        }

        if (!any)
            return Array.Empty<CSharpToolDefinition>();

        return collector.Entries
            .Select(e => new CSharpToolDefinition(Project(e), e.Delegate))
            .ToArray();
    }

    private static ToolDescriptor Project(RegisteredFunctionEntry entry)
    {
        var description = entry.Description;

        IReadOnlyList<IParameterDescriptor>? parameters = null;
        if (entry.Delegate != null)
        {
            var fd = FunctionDescriptor.FromDelegate(entry.Delegate, entry.Description);
            parameters = fd.Parameters;
            description = fd.Description;
        }
        else if (entry.Parameters != null)
        {
            parameters = entry.Parameters
                .Select(p => (IParameterDescriptor)new ParameterDescriptor
                {
                    Name = p.Name,
                    Type = p.Type,
                    IsOptional = p.IsOptional,
                    DefaultValue = p.DefaultValue,
                })
                .ToArray();
        }

        var inputs = new Dictionary<string, object?>();
        if (parameters != null)
        {
            foreach (var p in parameters.Where(p => p.Type != typeof(CancellationToken)))
            {
                inputs[p.Name] = new Dictionary<string, object?>
                {
                    ["type"] = TypeName(p.Type),
                    ["optional"] = p.IsOptional,
                };
            }
        }

        if (string.IsNullOrWhiteSpace(description))
            description = $"C# 函数库工具 {entry.Name}";

        var empty = new Dictionary<string, object?>();
        return new ToolDescriptor(
            Id: $"tool:csharp:{entry.Name}",
            Name: entry.Name,
            Backend: ToolBackend.CSharp,
            Version: 1,
            Description: description,
            InputSchema: inputs,
            OutputSchema: empty,
            Flags: ToolFlags.Deterministic | ToolFlags.HotReloadable);
    }

    private static string TypeName(Type type)
        => type.IsGenericType
            ? type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">"
            : type.FullName ?? type.Name;

    private static Type? ResolveType(string typeName)
    {
        // 1) assembly-qualified：默认 ALC 直接解析（库程序集已随宿主加载时命中）。
        var type = Type.GetType(typeName, throwOnError: false);
        if (type != null) return type;

        // 2) 兜底：在已加载程序集内按限定名查找（避免加载顺序依赖）。
        var (fullName, _) = SplitAssemblyQualified(typeName);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var hit = asm.GetType(fullName, throwOnError: false);
            if (hit != null) return hit;
        }

        return null;
    }

    private static (string FullName, string? Assembly) SplitAssemblyQualified(string q)
    {
        var i = q.IndexOf(',');
        return i < 0 ? (q.Trim(), null) : (q[..i].Trim(), q[(i + 1)..].Trim());
    }

    /// <summary>一次注册会话内的单个函数。</summary>
    private sealed record RegisteredFunctionEntry(
        string Name,
        string Description,
        Delegate? Delegate,
        IReadOnlyList<IParamConfig>? Parameters);

    /// <summary>
    /// 轻量 <see cref="ITrConfigRegistry"/> 收集器：捕获注册快照并做同名冲突检测，不执行、不承载运行状态。
    /// </summary>
    private sealed class Collector : ITrConfigRegistry
    {
        private readonly Dictionary<string, RegisteredFunctionEntry> _byName = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<RegisteredFunctionEntry> Entries => _byName.Values;

        public void Register(string name, ITrConfig config)
        {
            Add(name, new RegisteredFunctionEntry(name, config.Description, null,
                config.Parameters?.ToArray()));
        }

        public void Register(string name, Delegate function, string? description = null)
            => Add(name, new RegisteredFunctionEntry(name, description ?? string.Empty, function, null));

        public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
            => Add(name, new RegisteredFunctionEntry(name, description ?? string.Empty, function, parameters?.ToArray()));

        private void Add(string name, RegisteredFunctionEntry entry)
        {
            if (!_byName.TryAdd(name, entry))
                throw new InvalidOperationException(
                    $"C# 函数库源内函数名 '{name}' 重复注册。C#Track 工具名必须唯一（与 FunctionLibraryLoader 同名规则一致）。");
        }

        public void Unregister(string name) => _byName.Remove(name);

        public bool TryGetConfig(string name, out ITrConfig config)
        {
            config = default!;
            return false;
        }

        public bool Contains(string name) => _byName.ContainsKey(name);

        public IEnumerable<string> GetRegisteredNames() => _byName.Keys;

        public void Clear() => _byName.Clear();

        public Delegate? Find(string functionName)
            => _byName.TryGetValue(functionName, out var e) ? e.Delegate : null;

        public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor)
        {
            if (_byName.TryGetValue(name, out var e) && e.Delegate != null)
            {
                descriptor = FunctionDescriptor.FromDelegate(e.Delegate, e.Description);
                return true;
            }

            descriptor = null;
            return false;
        }

        public bool TryGetDelegate(string name, out Delegate? function)
        {
            if (_byName.TryGetValue(name, out var e))
            {
                function = e.Delegate;
                return function != null;
            }

            function = null;
            return false;
        }
    }
}
