using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry.AutoRegister;
using Share.Registry.AutoRegister.Extractors;
using Share.Registry.AutoRegister.Filters;
using Share.Registry.AutoRegister.Naming;
using Share.Registry.AutoRegister.Description;
using Share.Registry.AutoRegister.Delegate;
using System.Reflection;

namespace Registry.Tests;

[TestClass]
public class AutoRegisterTests : TestBase
{

    [TestMethod]
    public void RunAll_ReflectionExtractor()
    {
        var testAssembly = typeof(TestMathClass).Assembly;
        var mathTypeName = typeof(TestMathClass).FullName!;
        var instanceTypeName = typeof(TestInstanceClass).FullName!;
        var outerTypeName = typeof(TestOuterClass).FullName!;

        var result = new TestGroupBuilder("ReflectionExtractor")
            .Add("ExtractMethods_ShouldFindPublicMethods", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var methods = extractor.ExtractMethods(mathTypeName);
                return methods.Count > 0
                    && methods.Any(m => m.MethodName == "Add")
                    && methods.Any(m => m.MethodName == "Multiply");
            })
            .Add("ExtractMethods_ShouldIncludeStaticFlag", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var methods = extractor.ExtractMethods(mathTypeName);
                return methods.All(m => m.IsStatic);
            })
            .Add("ExtractMethods_ShouldIncludeParameters", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var methods = extractor.ExtractMethods(mathTypeName);
                var addMethod = methods.First(m => m.MethodName == "Add");
                return addMethod.Parameters.Length == 2
                    && addMethod.Parameters[0].Name == "a"
                    && addMethod.Parameters[1].Name == "b";
            })
            .Add("ExtractMethods_WithFilter_ExcludeMethodNames", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var filter = new MethodFilterConfig { ExcludeMethodNames = new List<string> { "Multiply" } };
                var methods = extractor.ExtractMethods(mathTypeName, filter);
                return methods.All(m => m.MethodName != "Multiply");
            })
            .Add("ExtractMethods_WithFilter_IncludeStaticOnly", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var filter = new MethodFilterConfig { IncludeStaticOnly = true };
                var methods = extractor.ExtractMethods(instanceTypeName, filter);
                return methods.All(m => m.IsStatic);
            })
            .Add("ExtractMethods_WithFilter_MaxParams", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var filter = new MethodFilterConfig { MaxParams = 1 };
                var methods = extractor.ExtractMethods(mathTypeName, filter);
                return methods.All(m => m.Parameters.Length <= 1);
            })
            .Add("ExtractMethods_WithFilter_ExcludeByReturnType", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var filter = new MethodFilterConfig { ExcludeByReturnType = new List<string> { "Void" } };
                var methods = extractor.ExtractMethods(mathTypeName, filter);
                return methods.All(m => m.ReturnType.Name != "Void");
            })
            .Add("ExtractMethods_NestedClass_ShouldIncludeNestedMethods", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var methods = extractor.ExtractMethods(outerTypeName);
                return methods.Any(m => m.ContainingNestedClassName == "InnerClass");
            })
            .Add("ExtractMethods_NestedClass_WithExcludeFilter", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                var filter = new MethodFilterConfig { ExcludeNestedClasses = new List<string> { "InnerClass" } };
                var methods = extractor.ExtractMethods(outerTypeName, filter);
                return methods.All(m => m.ContainingNestedClassName != "InnerClass");
            })
            .Add("ExtractMethods_TypeNotFound_ShouldThrow", () =>
            {
                var extractor = new ReflectionExtractor(testAssembly);
                try
                {
                    extractor.ExtractMethods("NonExistentType");
                    return false;
                }
                catch (InvalidOperationException) { return true; }
            })
            .Run();

        result.AssertAllPassed();
    }


    [TestMethod]
    public void RunAll_RoslynExtractor()
    {
        var sourceCode = @"
using System;

namespace TestNamespace
{
    public class Calculator
    {
        /// <summary>Adds two numbers</summary>
        public static int Add(int a, int b) => a + b;

        public static double Multiply(double x, double y) => x * y;

        private static void Helper() { }

        public static async System.Threading.Tasks.Task<int> GetAsync() { await System.Threading.Tasks.Task.Delay(1); return 42; }
    }
}";
        var result = new TestGroupBuilder("RoslynExtractor")
            .Add("ExtractMethods_ShouldFindPublicMethods", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Calculator");
                return methods.Count >= 3
                    && methods.Any(m => m.MethodName == "Add")
                    && methods.Any(m => m.MethodName == "Multiply")
                    && methods.Any(m => m.MethodName == "GetAsync");
            })
            .Add("ExtractMethods_ShouldIncludeStaticFlag", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Calculator");
                return methods.All(m => m.IsStatic);
            })
            .Add("ExtractMethods_ShouldIncludeParameters", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Calculator");
                var addMethod = methods.First(m => m.MethodName == "Add");
                return addMethod.Parameters.Length == 2
                    && addMethod.Parameters[0].Name == "a"
                    && addMethod.Parameters[1].Name == "b";
            })
            .Add("ExtractMethods_ShouldIncludeDescription", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Calculator");
                var addMethod = methods.First(m => m.MethodName == "Add");
                return addMethod.Description.Contains("Adds two numbers");
            })
            .Add("ExtractMethods_ShouldDetectAsync", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Calculator");
                var asyncMethod = methods.First(m => m.MethodName == "GetAsync");
                return asyncMethod.IsAsync;
            })
            .Add("ExtractMethods_ShouldCreateExecutor", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Calculator");
                var addMethod = methods.First(m => m.MethodName == "Add");
                return addMethod.Executor != null;
            })
            .Add("ExtractMethods_Executor_ShouldExecuteCorrectly", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Calculator");
                var addMethod = methods.First(m => m.MethodName == "Add");
                var resultVal = addMethod.Executor!(new object[] { 3, 4 });
                return resultVal.Result is int sum && sum == 7;
            })
            .Add("ExtractMethods_WithFilter_ExcludeMethodNames", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var filter = new MethodFilterConfig { ExcludeMethodNames = new List<string> { "Multiply" } };
                var methods = extractor.ExtractMethods("Calculator", filter);
                return methods.All(m => m.MethodName != "Multiply");
            })
            .Add("ExtractMethods_TypeNotFound_ShouldThrow", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                try
                {
                    extractor.ExtractMethods("NonExistent");
                    return false;
                }
                catch (InvalidOperationException) { return true; }
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_RoslynExtractor_NestedClass()
    {
        var sourceCode = @"
using System;

namespace TestNamespace
{
    public class Outer
    {
        public static void OuterMethod() { }

        public class Inner
        {
            public static void InnerMethod() { }
        }
    }
}";
        var result = new TestGroupBuilder("RoslynExtractor_NestedClass")
            .Add("ExtractMethods_ShouldIncludeNestedMethods", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Outer");
                return methods.Any(m => m.MethodName == "OuterMethod")
                    && methods.Any(m => m.MethodName == "InnerMethod");
            })
            .Add("ExtractMethods_NestedMethod_ShouldHaveNestedClassName", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var methods = extractor.ExtractMethods("Outer");
                var innerMethod = methods.First(m => m.MethodName == "InnerMethod");
                return innerMethod.ContainingNestedClassName == "Inner";
            })
            .Add("ExtractMethods_WithExcludeNestedFilter", () =>
            {
                var extractor = new RoslynExtractor(sourceCode);
                var filter = new MethodFilterConfig { ExcludeNestedClasses = new List<string> { "Inner" } };
                var methods = extractor.ExtractMethods("Outer", filter);
                return methods.All(m => m.ContainingNestedClassName != "Inner");
            })
            .Run();

        result.AssertAllPassed();
    }


    [TestMethod]
    public void RunAll_NamingConventions()
    {
        var result = new TestGroupBuilder("NamingConventions")
            .Add("MethodNameOnly_ShouldReturnMethodName", () =>
            {
                var naming = NamingConventions.MethodNameOnly;
                var name = naming.GenerateName("Maths", null, "Add", new NamingConfig());
                return name == "Add";
            })
            .Add("NestedClass_MethodName_WithNested_ShouldUseSeparator", () =>
            {
                var naming = NamingConventions.NestedClass_MethodName;
                var name = naming.GenerateName("Maths", "Activation", "ReLU", new NamingConfig());
                return name == "Activation_ReLU";
            })
            .Add("NestedClass_MethodName_WithoutNested_ShouldReturnMethodName", () =>
            {
                var naming = NamingConventions.NestedClass_MethodName;
                var name = naming.GenerateName("Maths", null, "Add", new NamingConfig());
                return name == "Add";
            })
            .Add("NestedClass_DotMethodName_WithNested_ShouldUseDot", () =>
            {
                var naming = NamingConventions.NestedClass_DotMethodName;
                var name = naming.GenerateName("Maths", "Activation", "ReLU", new NamingConfig());
                return name == "Activation.ReLU";
            })
            .Add("NestedClass_DotMethodName_WithCustomSeparator", () =>
            {
                var naming = NamingConventions.NestedClass_DotMethodName;
                var config = new NamingConfig { Separator = "::" };
                var name = naming.GenerateName("Maths", "Activation", "ReLU", config);
                return name == "Activation::ReLU";
            })
            .Add("NestedClass_MethodName_WithCustomSeparator", () =>
            {
                var naming = NamingConventions.NestedClass_MethodName;
                var config = new NamingConfig { Separator = "-" };
                var name = naming.GenerateName("Maths", "Activation", "ReLU", config);
                return name == "Activation-ReLU";
            })
            .Add("WithPrefix_ShouldPrependPrefix", () =>
            {
                var naming = NamingConventions.MethodNameOnly;
                var config = new NamingConfig { Prefix = "math_" };
                var name = naming.GenerateName("Maths", null, "Add", config);
                return name == "math_Add";
            })
            .Add("WithSuffix_ShouldAppendSuffix", () =>
            {
                var naming = NamingConventions.MethodNameOnly;
                var config = new NamingConfig { Suffix = "_v1" };
                var name = naming.GenerateName("Maths", null, "Add", config);
                return name == "Add_v1";
            })
            .Add("GetByName_ShouldReturnCorrectConvention", () =>
            {
                return NamingConventions.GetByName("NestedClass_MethodName").Name == "NestedClass_MethodName"
                    && NamingConventions.GetByName("NestedClass.MethodName").Name == "NestedClass.MethodName"
                    && NamingConventions.GetByName("MethodName").Name == "MethodName"
                    && NamingConventions.GetByName("Unknown").Name == "MethodName";
            })
            .Run();

        result.AssertAllPassed();
    }


    [TestMethod]
    public void RunAll_MethodFilter()
    {
        var methods = new List<ExtractedMethodInfo>
        {
            new() { MethodName = "Add", ReturnType = typeof(int), Parameters = Array.Empty<ExtractedMethodInfo.ParameterInfo>(), IsStatic = true },
            new() { MethodName = "VoidMethod", ReturnType = typeof(void), Parameters = Array.Empty<ExtractedMethodInfo.ParameterInfo>(), IsStatic = true },
            new() { MethodName = "MultiParam", ReturnType = typeof(string), Parameters = new[] { new ExtractedMethodInfo.ParameterInfo(), new ExtractedMethodInfo.ParameterInfo() }, IsStatic = true },
            new() { MethodName = "InstanceMethod", ReturnType = typeof(int), Parameters = Array.Empty<ExtractedMethodInfo.ParameterInfo>(), IsStatic = false },
            new() { MethodName = "NestedMethod", ReturnType = typeof(double), Parameters = Array.Empty<ExtractedMethodInfo.ParameterInfo>(), IsStatic = true, ContainingNestedClassName = "Inner" },
        };

        var result = new TestGroupBuilder("MethodFilter")
            .Add("NullConfig_ShouldReturnAll", () =>
            {
                var filtered = MethodFilter.Apply(methods, null);
                return filtered.Count == methods.Count;
            })
            .Add("ExcludeMethodNames_ShouldExclude", () =>
            {
                var config = new MethodFilterConfig { ExcludeMethodNames = new List<string> { "Add", "VoidMethod" } };
                var filtered = MethodFilter.Apply(methods, config);
                return filtered.Count == 3 && filtered.All(m => m.MethodName != "Add" && m.MethodName != "VoidMethod");
            })
            .Add("IncludeStaticOnly_ShouldExcludeInstanceMethods", () =>
            {
                var config = new MethodFilterConfig { IncludeStaticOnly = true };
                var filtered = MethodFilter.Apply(methods, config);
                return filtered.All(m => m.IsStatic);
            })
            .Add("MaxParams_ShouldFilterByParamCount", () =>
            {
                var config = new MethodFilterConfig { MaxParams = 0 };
                var filtered = MethodFilter.Apply(methods, config);
                return filtered.All(m => m.Parameters.Length == 0);
            })
            .Add("ExcludeByReturnType_ShouldExclude", () =>
            {
                var config = new MethodFilterConfig { ExcludeByReturnType = new List<string> { "Void" } };
                var filtered = MethodFilter.Apply(methods, config);
                return filtered.All(m => m.ReturnType.Name != "Void");
            })
            .Add("ExcludeNestedClasses_ShouldExcludeNested", () =>
            {
                var config = new MethodFilterConfig { ExcludeNestedClasses = new List<string> { "Inner" } };
                var filtered = MethodFilter.Apply(methods, config);
                return filtered.All(m => m.ContainingNestedClassName != "Inner");
            })
            .Add("IncludeNestedClasses_ShouldOnlyIncludeSpecified", () =>
            {
                var config = new MethodFilterConfig { IncludeNestedClasses = new List<string> { "Inner" } };
                var filtered = MethodFilter.Apply(methods, config);
                return filtered.All(m => m.ContainingNestedClassName == "Inner" || m.ContainingNestedClassName == null);
            })
            .Run();

        result.AssertAllPassed();
    }


    [TestMethod]
    public void RunAll_DescriptionProviders()
    {
        var methodWithDesc = new ExtractedMethodInfo
        {
            MethodName = "Test",
            Description = "This is a test method",
            ReturnType = typeof(void)
        };
        var methodWithoutDesc = new ExtractedMethodInfo
        {
            MethodName = "NoDesc",
            Description = string.Empty,
            ReturnType = typeof(void)
        };

        var result = new TestGroupBuilder("DescriptionProviders")
            .Add("Default_ShouldReturnDescription", () =>
            {
                var provider = DescriptionProviders.Default;
                return provider.GetDescription(methodWithDesc) == "This is a test method";
            })
            .Add("Default_WithoutDescription_ShouldReturnEmpty", () =>
            {
                var provider = DescriptionProviders.Default;
                return provider.GetDescription(methodWithoutDesc) == string.Empty;
            })
            .Add("None_ShouldReturnEmpty", () =>
            {
                var provider = DescriptionProviders.None;
                return provider.GetDescription(methodWithDesc) == string.Empty;
            })
            .Add("GetByName_ShouldReturnCorrectProvider", () =>
            {
                return DescriptionProviders.GetByName("xmlDoc").SourceType == "default"
                    && DescriptionProviders.GetByName("attribute").SourceType == "default"
                    && DescriptionProviders.GetByName("none").SourceType == "none"
                    && DescriptionProviders.GetByName("unknown").SourceType == "none";
            })
            .Run();

        result.AssertAllPassed();
    }


    [TestMethod]
    public void RunAll_DelegateFactory()
    {
        var factory = new DelegateFactory();

        var result = new TestGroupBuilder("DelegateFactory")
            .Add("CreateExecutor_StaticMethod_ShouldWork", () =>
            {
                var methodInfo = typeof(TestMathClass).GetMethod("Add")!;
                var executor = factory.CreateExecutor(methodInfo);
                var task = executor(new object[] { 3, 4 });
                return task.Result is int sum && sum == 7;
            })
            .Add("CreateExecutor_StaticMethod_WithStringResult", () =>
            {
                var methodInfo = typeof(TestMathClass).GetMethod("Greet")!;
                var executor = factory.CreateExecutor(methodInfo);
                var task = executor(new object[] { "World" });
                return task.Result is string msg && msg == "Hello, World!";
            })
            .Add("CreateExecutor_InstanceMethod_ShouldWork", () =>
            {
                var methodInfo = typeof(TestInstanceClass).GetMethod("GetValue")!;
                var executor = factory.CreateExecutor(methodInfo);
                var instance = new TestInstanceClass(42);
                var task = executor(new object[] { instance });
                return task.Result is int val && val == 42;
            })
            .Add("CreateExecutor_AsyncMethod_ShouldReturnTask", () =>
            {
                var methodInfo = typeof(TestAsyncClass).GetMethod("GetAsyncValue")!;
                var executor = factory.CreateExecutor(methodInfo);
                var task = executor(Array.Empty<object>());
                return task.Result is int val && val == 99;
            })
            .Run();

        result.AssertAllPassed();
    }


    [TestMethod]
    public void RunAll_AutoRegisterService_Assembly()
    {
        var service = new AutoRegisterService();
        var assembly = typeof(TestMathClass).Assembly;
        var mathTypeName = typeof(TestMathClass).FullName!;
        var outerTypeName = typeof(TestOuterClass).FullName!;

        var entry = new RegistrationEntry
        {
            Source = new SourceConfig { Type = "assembly", Path = assembly.Location },
            ClassName = mathTypeName,
            Naming = new NamingConfig { Convention = "MethodName" },
            Description = new DescriptionConfig { Source = "none" }
        };

        var result = new TestGroupBuilder("AutoRegisterService_Assembly")
            .Add("ProcessAssembly_ShouldReturnMethods", () =>
            {
                var autoResult = service.ProcessAssembly(assembly, entry);
                return autoResult.TotalCount > 0;
            })
            .Add("ProcessAssembly_ShouldIncludeMethodNames", () =>
            {
                var autoResult = service.ProcessAssembly(assembly, entry);
                return autoResult.RegisteredItems.Any(i => i.MethodName == "Add")
                    && autoResult.RegisteredItems.Any(i => i.MethodName == "Multiply");
            })
            .Add("ProcessAssembly_ShouldIncludeExecutor", () =>
            {
                var autoResult = service.ProcessAssembly(assembly, entry);
                var addItem = autoResult.RegisteredItems.First(i => i.MethodName == "Add");
                return addItem.Executor != null;
            })
            .Add("ProcessAssembly_WithNaming_NestedClass_MethodName", () =>
            {
                var outerEntry = new RegistrationEntry
                {
                    Source = new SourceConfig { Type = "assembly", Path = assembly.Location },
                    ClassName = outerTypeName,
                    Naming = new NamingConfig { Convention = "NestedClass_MethodName" },
                    Description = new DescriptionConfig { Source = "none" }
                };
                var autoResult = service.ProcessAssembly(assembly, outerEntry);
                var innerItem = autoResult.RegisteredItems.FirstOrDefault(i => i.MethodName == "InnerMethod");
                return innerItem != null && innerItem.RegisteredName == "InnerClass_InnerMethod";
            })
            .Add("ProcessAssembly_WithFilter_ExcludeMethodNames", () =>
            {
                var filteredEntry = new RegistrationEntry
                {
                    Source = new SourceConfig { Type = "assembly", Path = assembly.Location },
                    ClassName = mathTypeName,
                    MethodFilters = new MethodFilterConfig { ExcludeMethodNames = new List<string> { "Multiply" } }
                };
                var autoResult = service.ProcessAssembly(assembly, filteredEntry);
                return autoResult.RegisteredItems.All(i => i.MethodName != "Multiply");
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_AutoRegisterService_Source()
    {
        var sourceCode = @"
using System;

namespace TestNamespace
{
    public class TestService
    {
        /// <summary>Adds two numbers</summary>
        public static int Add(int a, int b) => a + b;

        public static string Greet(string name) => $""Hello, {name}!"";
    }
}";
        var service = new AutoRegisterService();

        var entry = new RegistrationEntry
        {
            Source = new SourceConfig { Type = "source" },
            ClassName = "TestService",
            Naming = new NamingConfig { Convention = "MethodName" },
            Description = new DescriptionConfig { Source = "xmlDoc" }
        };

        var result = new TestGroupBuilder("AutoRegisterService_Source")
            .Add("ProcessSource_ShouldReturnMethods", () =>
            {
                var autoResult = service.ProcessSource(sourceCode, entry);
                return autoResult.TotalCount > 0;
            })
            .Add("ProcessSource_ShouldIncludeMethodNames", () =>
            {
                var autoResult = service.ProcessSource(sourceCode, entry);
                return autoResult.RegisteredItems.Any(i => i.MethodName == "Add")
                    && autoResult.RegisteredItems.Any(i => i.MethodName == "Greet");
            })
            .Add("ProcessSource_ShouldIncludeDescription", () =>
            {
                var autoResult = service.ProcessSource(sourceCode, entry);
                var addItem = autoResult.RegisteredItems.First(i => i.MethodName == "Add");
                return addItem.Description.Contains("Adds two numbers");
            })
            .Add("ProcessSource_ShouldIncludeExecutor", () =>
            {
                var autoResult = service.ProcessSource(sourceCode, entry);
                var addItem = autoResult.RegisteredItems.First(i => i.MethodName == "Add");
                return addItem.Executor != null;
            })
            .Add("ProcessSource_Executor_ShouldExecuteCorrectly", () =>
            {
                var autoResult = service.ProcessSource(sourceCode, entry);
                var addItem = autoResult.RegisteredItems.First(i => i.MethodName == "Add");
                var task = addItem.Executor!(new object[] { 3, 4 });
                return task.Result is int sum && sum == 7;
            })
            .Add("ProcessSource_WithNaming_NestedClass_MethodName", () =>
            {
                var nestedSource = @"
using System;
namespace TestNamespace
{
    public class OuterService
    {
        public static void OuterMethod() { }
        public class InnerService
        {
            public static void InnerMethod() { }
        }
    }
}";
                var nestedEntry = new RegistrationEntry
                {
                    Source = new SourceConfig { Type = "source" },
                    ClassName = "OuterService",
                    Naming = new NamingConfig { Convention = "NestedClass_MethodName" }
                };
                var autoResult = service.ProcessSource(nestedSource, nestedEntry);
                var innerItem = autoResult.RegisteredItems.FirstOrDefault(i => i.MethodName == "InnerMethod");
                return innerItem != null && innerItem.RegisteredName == "InnerService_InnerMethod";
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_AutoRegisterService_Batch()
    {
        var sourceCode = @"
using System;
namespace TestNamespace
{
    public class BatchClass
    {
        public static int Add(int a, int b) => a + b;
        public static int Subtract(int a, int b) => a - b;
    }
}";
        var service = new AutoRegisterService();
        var config = new AutoRegisterConfig
        {
            Registrations = new List<RegistrationEntry>
            {
                new()
                {
                    Source = new SourceConfig { Type = "source" },
                    ClassName = "BatchClass",
                    Naming = new NamingConfig { Convention = "MethodName" }
                }
            }
        };

        var result = new TestGroupBuilder("AutoRegisterService_Batch")
            .Add("ProcessBatch_ShouldReturnResults", () =>
            {
                var results = service.ProcessBatch(config, resolveSource: _ => sourceCode);
                return results.Count == 1 && results[0].Result.TotalCount > 0;
            })
            .Add("ProcessBatch_ShouldIncludeAllMethods", () =>
            {
                var results = service.ProcessBatch(config, resolveSource: _ => sourceCode);
                var item = results[0].Result;
                return item.RegisteredItems.Any(i => i.MethodName == "Add")
                    && item.RegisteredItems.Any(i => i.MethodName == "Subtract");
            })
            .Add("ProcessBatch_WithUnknownType_ShouldFail", () =>
            {
                var badConfig = new AutoRegisterConfig
                {
                    Registrations = new List<RegistrationEntry>
                    {
                        new()
                        {
                            Source = new SourceConfig { Type = "unknown" },
                            ClassName = "Test"
                        }
                    }
                };
                var results = service.ProcessBatch(badConfig);
                return results[0].Result.FailCount > 0;
            })
            .Run();

        result.AssertAllPassed();
    }
}


public class TestMathClass
{
    public static int Add(int a, int b) => a + b;
    public static double Multiply(double x, double y) => x * y;
    public static void DoNothing() { }
    public static string Greet(string name) => $"Hello, {name}!";
}

public class TestInstanceClass
{
    private readonly int _value;
    public TestInstanceClass(int value) { _value = value; }
    public int GetValue() => _value;
    public static string StaticGreet() => "Hi!";
}

public class TestAsyncClass
{
    public static async System.Threading.Tasks.Task<int> GetAsyncValue()
    {
        await System.Threading.Tasks.Task.Delay(1);
        return 99;
    }
}

public class TestOuterClass
{
    public static void OuterMethod() { }
    public class InnerClass
    {
        public static void InnerMethod() { }
    }
}
