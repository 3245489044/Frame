
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Registry;
using TConfig;

namespace WorkFlowMethod.Tests.UsageTests;

[TestClass]
[TestCategory("FunctionLibraryLifecycle")]
public class FunctionLibraryLifecycleTests
{
    private FunctionRegistry? _registry;
    private FunctionLibraryLifecycle? _lifecycle;

    [TestInitialize]
    public void Setup()
    {
        _registry = new FunctionRegistry();
        _lifecycle = new FunctionLibraryLifecycle();
    }


    [TestMethod]
    public void AllLibraryTypes_ShouldBeResolvableByTypeGetType()
    {
        var libraryTypes = GetLibraryTypeNamesFromConfig();

        foreach (var (name, typeName) in libraryTypes)
        {
            var type = Type.GetType(typeName);
            Assert.IsNotNull(type,
                $"类型 '{typeName}' (库: {name}) 无法通过 Type.GetType() 解析。\n" +
                $"请检查 JSON 配置中的 type 字段是否正确，以及程序集是否已加载。");
        }
    }

    [TestMethod]
    public void AllLibraryTypes_ShouldHaveRegisterAllMethod()
    {
        var libraryTypes = GetLibraryTypeNamesFromConfig();

        foreach (var (name, typeName) in libraryTypes)
        {
            var type = Type.GetType(typeName);
            Assert.IsNotNull(type, $"类型 '{typeName}' 无法解析");

            var method = type!.GetMethod("RegisterAll",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(IFunctionRegistry) },
                null);

            if (method == null)
            {
                method = type.GetMethod("Register",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    System.Type.EmptyTypes,
                    null);
            }

            Assert.IsNotNull(method,
                $"类型 '{typeName}' (库: {name}) 没有 RegisterAll(IFunctionRegistry) 或 Register() 静态方法。");
        }
    }


    [TestMethod]
    public void Initialize_ShouldRegisterMathLibraryFunctions()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;

        InjectRegistryToLifecycle(lifecycle, registry);

        lifecycle.Initialize();

        Assert.IsTrue(registry.Contains("Add"), "Add 函数应已注册");
        Assert.IsTrue(registry.Contains("Subtract"), "Subtract 函数应已注册");
        Assert.IsTrue(registry.Contains("Multiply"), "Multiply 函数应已注册");
        Assert.IsTrue(registry.Contains("Divide"), "Divide 函数应已注册");
    }

    [TestMethod]
    public void Initialize_ShouldRegisterBuiltInUnitsLibraryFunctions()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);

        lifecycle.Initialize();

        Assert.IsTrue(registry.Contains("StringToInt"), "StringToInt 函数应已注册");
        Assert.IsTrue(registry.Contains("StringToDouble"), "StringToDouble 函数应已注册");
        Assert.IsTrue(registry.Contains("StringToBool"), "StringToBool 函数应已注册");
        Assert.IsTrue(registry.Contains("JsonSerialize"), "JsonSerialize 函数应已注册");
        Assert.IsTrue(registry.Contains("JsonDeserialize"), "JsonDeserialize 函数应已注册");
    }

    [TestMethod]
    public void Initialize_ShouldRegisterMathBrowserFunctions()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);

        lifecycle.Initialize();

        Assert.IsTrue(registry.Contains("Math_GetDomains"), "Math_GetDomains 函数应已注册");
        Assert.IsTrue(registry.Contains("Math_GetFunctionsInDomain"), "Math_GetFunctionsInDomain 函数应已注册");
        Assert.IsTrue(registry.Contains("Math_GetFunctionDetail"), "Math_GetFunctionDetail 函数应已注册");
        Assert.IsTrue(registry.Contains("Math_Execute"), "Math_Execute 函数应已注册");
        Assert.IsTrue(registry.Contains("Math_Search"), "Math_Search 函数应已注册");
        Assert.IsTrue(registry.Contains("Math_GetStats"), "Math_GetStats 函数应已注册");
        Assert.IsTrue(registry.Contains("CopyToClipboard"), "CopyToClipboard 函数应已注册");
    }

    [TestMethod]
    public void Initialize_ShouldRegisterUIToolboxFunctions()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);

        lifecycle.Initialize();

        Assert.IsTrue(registry.Contains("ClearOutput"), "ClearOutput 函数应已注册");
        Assert.IsTrue(registry.Contains("SetValue"), "SetValue 函数应已注册");
        Assert.IsTrue(registry.Contains("ExecuteWorkflow"), "ExecuteWorkflow 函数应已注册");
        Assert.IsTrue(registry.Contains("ReloadWorkflows"), "ReloadWorkflows 函数应已注册");
        Assert.IsTrue(registry.Contains("SaveResultToFile"), "SaveResultToFile 函数应已注册");
        Assert.IsTrue(registry.Contains("LoadWorkflowDetails"), "LoadWorkflowDetails 函数应已注册");
        Assert.IsTrue(registry.Contains("LoadEnvironmentApiKey"), "LoadEnvironmentApiKey 函数应已注册");
        Assert.IsTrue(registry.Contains("LoadDefaultInput"), "LoadDefaultInput 函数应已注册");
        Assert.IsTrue(registry.Contains("GetWorkflowNames"), "GetWorkflowNames 函数应已注册");
    }


    [TestMethod]
    public void RegisteredAddFunction_ShouldBeExecutable()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);
        lifecycle.Initialize();

        var addExecuted = registry.TryGetDelegate("Add", out var addDelegate);

        Assert.IsTrue(addExecuted, "Add 函数的委托应可获取");
        Assert.IsNotNull(addDelegate, "Add 函数的委托不应为 null");

        if (addDelegate != null)
        {
            var result = addDelegate.DynamicInvoke(3.0, 5.0);
            Assert.AreEqual(8.0, result, "Add(3, 5) 应返回 8");
        }
    }

    [TestMethod]
    public void RegisteredStringToIntFunction_ShouldBeExecutable()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);
        lifecycle.Initialize();

        var found = registry.TryGetDelegate("StringToInt", out var del);

        Assert.IsTrue(found, "StringToInt 函数的委托应可获取");
        Assert.IsNotNull(del);

        if (del != null)
        {
            var result = del.DynamicInvoke("42");
            Assert.AreEqual(42, result, "StringToInt('42') 应返回 42");
        }
    }

    [TestMethod]
    public void RegisteredSetValueFunction_ShouldBeExecutable()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);
        lifecycle.Initialize();

        var found = registry.TryGetDelegate("SetValue", out var del);

        Assert.IsTrue(found, "SetValue 函数的委托应可获取");
        Assert.IsNotNull(del);

        if (del != null)
        {
            var result = del.DynamicInvoke("test_value");
            Assert.AreEqual("test_value", result, "SetValue('test_value') 应返回 'test_value'");
        }
    }


    [TestMethod]
    public void Initialize_Twice_ShouldNotDuplicateRegistrations()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);

        lifecycle.Initialize();
        var firstCount = registry.GetRegisteredNames().Count();

        lifecycle.Clear();
        lifecycle.Initialize();
        var secondCount = registry.GetRegisteredNames().Count();

        Assert.AreEqual(firstCount, secondCount,
            "重复 Initialize() 不应导致函数重复注册");
    }


    [TestMethod]
    public void AllEnabledLibraries_ShouldBeRegistered()
    {
        var registry = _registry!;
        var lifecycle = _lifecycle!;
        InjectRegistryToLifecycle(lifecycle, registry);

        lifecycle.Initialize();

        var registeredNames = registry.GetRegisteredNames().ToList();
        Assert.IsTrue(registeredNames.Count > 0,
            "初始化后注册表中应有函数");

        Assert.IsTrue(registeredNames.Contains("Add"));
        Assert.IsTrue(registeredNames.Contains("Subtract"));
        Assert.IsTrue(registeredNames.Contains("Multiply"));
        Assert.IsTrue(registeredNames.Contains("Divide"));
    }


    private static List<(string Name, string TypeName)> GetLibraryTypeNamesFromConfig()
    {
        var configPath = ResolveTestConfigPath();
        Assert.IsNotNull(configPath, "配置文件 function-libraries.json 未找到");

        var json = File.ReadAllText(configPath!);
        var root = System.Text.Json.JsonSerializer.Deserialize<LibraryConfigRoot>(json);
        Assert.IsNotNull(root, "配置文件解析失败");
        Assert.IsNotNull(root!.Libraries, "配置文件中没有 libraries 字段");
        Assert.IsTrue(root.Libraries!.Count > 0, "配置文件中 libraries 列表为空");

        return root.Libraries
            .Where(l => l.Enabled)
            .Select(l => (l.Name, l.TypeName))
            .ToList();
    }

    private static string? ResolveTestConfigPath()
    {
        var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "function-libraries.json");
        if (File.Exists(basePath))
            return basePath;

        var projectRoot = PathManager.ProjectPathManager.FindProjectRoot();
        if (!string.IsNullOrEmpty(projectRoot))
        {
            var projectPath = Path.Combine(projectRoot,
                "FileDriven", "DrivenConfig", "Configs", "function-libraries.json");
            if (File.Exists(projectPath))
                return projectPath;
        }

        return null;
    }

    private static void InjectRegistryToLifecycle(FunctionLibraryLifecycle lifecycle, FunctionRegistry registry)
    {
        var instance = Modules.Registry.UnifiedRegistryCenter.Instance;
        var functionsField = typeof(Modules.Registry.UnifiedRegistryCenter)
            .GetField("<Functions>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (functionsField != null)
        {
            functionsField.SetValue(instance, registry);
        }
        else
        {
            var prop = typeof(Modules.Registry.UnifiedRegistryCenter)
                .GetProperty("Functions",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop?.CanWrite == true)
            {
                prop.SetValue(instance, registry);
            }
        }
    }


    private class LibraryConfigRoot
    {
        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("libraries")]
        public List<LibraryConfigItem>? Libraries { get; set; }
    }

    private class LibraryConfigItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string TypeName { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("method")]
        public string MethodName { get; set; } = "RegisterAll";

        [System.Text.Json.Serialization.JsonPropertyName("hasRegistryParam")]
        public bool HasRegistryParam { get; set; } = true;

        [System.Text.Json.Serialization.JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
    }
}
