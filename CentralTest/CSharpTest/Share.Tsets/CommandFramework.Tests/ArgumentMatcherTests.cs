using TestInfrastructure;
using ParameterResolver.Abstractions;
using ParameterResolver.Builder;
using ParameterResolver.Converter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class ArgumentMatcherTests : TestBase
    {
        [TestMethod]
        public void RunAll_BuildArguments()
        {
            var result = new TestGroupBuilder("BuildArguments")
                .Add("BuildArguments_NoParams_ShouldReturnEmpty", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>();
                    var buildResult = builder.Build(parameters, null);
                    return buildResult.IsSuccess && buildResult.Args.Length == 0;
                })
                .Add("BuildArguments_DictionaryExact_ShouldMatch", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("name", typeof(string)),
                        new TestParamDescriptor("age", typeof(int))
                    };
                    var buildResult = builder.Build(parameters,
                        new Dictionary<string, object?>
                        {
                            ["name"] = "Alice",
                            ["age"] = 30
                        });
                    return buildResult.IsSuccess
                        && buildResult.Args[0]?.ToString() == "Alice"
                        && buildResult.Args[1]?.ToString() == "30";
                })
                .Add("BuildArguments_Positional_ShouldMatch", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("a", typeof(int)),
                        new TestParamDescriptor("b", typeof(int))
                    };
                    var buildResult = builder.Build(parameters,
                        new object[] { 10, 20 });
                    return buildResult.IsSuccess
                        && buildResult.Args[0]?.ToString() == "10"
                        && buildResult.Args[1]?.ToString() == "20";
                })
                .Add("BuildArguments_SingleValue_ShouldMatch", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("value", typeof(string))
                    };
                    var buildResult = builder.Build(parameters, "hello");
                    return buildResult.IsSuccess
                        && buildResult.Args[0]?.ToString() == "hello";
                })
                .Add("BuildArguments_DefaultValue_ShouldFill", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("name", typeof(string)),
                        new TestParamDescriptor("greeting", typeof(string), isOptional: true, defaultValue: "Hello")
                    };
                    var buildResult = builder.Build(parameters,
                        new Dictionary<string, object?>
                        {
                            ["name"] = "Bob"
                        });
                    return buildResult.IsSuccess
                        && buildResult.Args[0]?.ToString() == "Bob"
                        && buildResult.Args[1]?.ToString() == "Hello";
                })
                .Add("BuildArguments_NullForOptionalReferenceType_ShouldSucceed", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("optional", typeof(string), isOptional: true)
                    };
                    var buildResult = builder.Build(parameters, null);
                    return buildResult.IsSuccess && buildResult.Args[0] == null;
                })
                .Add("BuildArguments_NullForValueType_ShouldHaveConversionError", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("required", typeof(int))
                    };
                    var buildResult = builder.Build(parameters, null);
                    return !buildResult.IsSuccess;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_TypeConverter()
        {
            var result = new TestGroupBuilder("TypeConverter")
                .Add("Convert_StringToString_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("hello", typeof(string));
                    return error == null && converted?.ToString() == "hello";
                })
                .Add("Convert_StringToInt_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("42", typeof(int));
                    return error == null && converted is int i && i == 42;
                })
                .Add("Convert_StringToBool_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("true", typeof(bool));
                    return error == null && converted is true;
                })
                .Add("Convert_StringToDouble_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("3.14", typeof(double));
                    return error == null && converted is double d && Math.Abs(d - 3.14) < 0.001;
                })
                .Add("Convert_StringToLong_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("9999999999", typeof(long));
                    return error == null && converted is long l && l == 9999999999;
                })
                .Add("Convert_StringToDateTime_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("2024-01-15", typeof(DateTime));
                    return error == null && converted is DateTime dt && dt.Year == 2024;
                })
                .Add("Convert_StringToGuid_ShouldSucceed", () =>
                {
                    var guid = Guid.NewGuid().ToString();
                    var (converted, error) = TypeConverter.Convert(guid, typeof(Guid));
                    return error == null && converted is Guid;
                })
                .Add("Convert_NullToNullable_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert(null, typeof(int?));
                    return error == null && converted == null;
                })
                .Add("Convert_NullToNonNullable_ShouldFail", () =>
                {
                    var (converted, error) = TypeConverter.Convert(null, typeof(int));
                    return error != null;
                })
                .Add("Convert_InvalidInt_ShouldFail", () =>
                {
                    var (converted, error) = TypeConverter.Convert("notANumber", typeof(int));
                    return error != null;
                })
                .Add("Convert_Enum_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("Do", typeof(JsonVerb));
                    return error == null && converted is JsonVerb v && v == JsonVerb.Do;
                })
                .Add("Convert_EnumByInt_ShouldSucceed", () =>
                {
                    var (converted, error) = TypeConverter.Convert("0", typeof(JsonVerb));
                    return error == null && converted is JsonVerb v && v == JsonVerb.Do;
                })
                .Add("RegisterParser_ShouldWork", () =>
                {
                    TypeConverter.RegisterParser<MyCustomType>(s =>
                    {
                        if (s == "custom") return (true, new MyCustomType { Value = "parsed" });
                        return (false, null);
                    });
                    var (converted, error) = TypeConverter.Convert("custom", typeof(MyCustomType));
                    return error == null && converted is MyCustomType c && c.Value == "parsed";
                })
                .Run();

            result.AssertAllPassed();
        }
    }


    public class MyCustomType
    {
        public string Value { get; set; } = "";
    }

    public class TestParamDescriptor : IParameterDescriptor
    {
        public string Name { get; }
        public Type Type { get; }
        public bool IsOptional { get; }
        public object? DefaultValue { get; }

        public TestParamDescriptor(string name, Type type, bool isOptional = false, object? defaultValue = null)
        {
            Name = name;
            Type = type;
            IsOptional = isOptional;
            DefaultValue = defaultValue;
        }
    }
}
