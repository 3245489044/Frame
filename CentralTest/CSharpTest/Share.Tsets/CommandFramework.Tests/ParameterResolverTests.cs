using TestInfrastructure;
using ParameterResolver.Abstractions;
using ParameterResolver.Builder;
using ParameterResolver.Converter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class ParameterResolverTests : TestBase
    {
        [TestMethod]
        public void RunAll_ParameterResolver()
        {
            var result = new TestGroupBuilder("ParameterResolver")
                .Add("DictionaryMatcher_ExactMatch_ShouldSucceed", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("name", typeof(string)),
                        new TestParamDescriptor("age", typeof(int))
                    };
                    var dict = new Dictionary<string, object?>
                    {
                        ["name"] = "Alice",
                        ["age"] = 30
                    };
                    var result2 = builder.Build(parameters, dict);
                    return result2.IsSuccess
                        && result2.Args.Length == 2
                        && result2.Args[0]?.ToString() == "Alice"
                        && (int)result2.Args[1]! == 30;
                })
                .Add("DictionaryMatcher_PartialMatch_ShouldFail", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("name", typeof(string)),
                        new TestParamDescriptor("age", typeof(int))
                    };
                    var dict = new Dictionary<string, object?>
                    {
                        ["name"] = "Alice"
                    };
                    var result2 = builder.Build(parameters, dict);
                    return !result2.IsSuccess;
                })
                .Add("DictionaryMatcher_OptionalParam_ShouldSucceed", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("name", typeof(string)),
                        new TestParamDescriptor("age", typeof(int), isOptional: true, defaultValue: 18)
                    };
                    var dict = new Dictionary<string, object?>
                    {
                        ["name"] = "Alice"
                    };
                    var result2 = builder.Build(parameters, dict);
                    return result2.IsSuccess
                        && result2.Args.Length == 2
                        && result2.Args[0]?.ToString() == "Alice"
                        && (int)result2.Args[1]! == 18;
                })
                .Add("PositionalMatcher_StringArray_ShouldSucceed", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("a", typeof(string)),
                        new TestParamDescriptor("b", typeof(int))
                    };
                    var values = new string?[] { "hello", "42" };
                    var result2 = builder.Build(parameters, values);
                    return result2.IsSuccess
                        && result2.Args[0]?.ToString() == "hello"
                        && (int)result2.Args[1]! == 42;
                })
                .Add("PositionalMatcher_ObjectArray_ShouldSucceed", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("x", typeof(int)),
                        new TestParamDescriptor("y", typeof(string))
                    };
                    var values = new object?[] { 123, "test" };
                    var result2 = builder.Build(parameters, values);
                    return result2.IsSuccess
                        && (int)result2.Args[0]! == 123
                        && result2.Args[1]?.ToString() == "test";
                })
                .Add("PositionalMatcher_TooFewValues_ShouldFail", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("a", typeof(string)),
                        new TestParamDescriptor("b", typeof(int))
                    };
                    var values = new string?[] { "only" };
                    var result2 = builder.Build(parameters, values);
                    return !result2.IsSuccess;
                })
                .Add("SingleValueMatcher_SingleParam_ShouldSucceed", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("value", typeof(string))
                    };
                    var result2 = builder.Build(parameters, "singleValue");
                    return result2.IsSuccess
                        && result2.Args.Length == 1
                        && result2.Args[0]?.ToString() == "singleValue";
                })
                .Add("SingleValueMatcher_MultiParams_ShouldFail", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("a", typeof(string)),
                        new TestParamDescriptor("b", typeof(int))
                    };
                    var result2 = builder.Build(parameters, "value");
                    return !result2.IsSuccess;
                })
                .Add("DefaultValueMatcher_AllOptional_ShouldSucceed", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("a", typeof(string), isOptional: true, defaultValue: "default"),
                        new TestParamDescriptor("b", typeof(int), isOptional: true, defaultValue: 99)
                    };
                    var result2 = builder.Build(parameters, null);
                    return result2.IsSuccess
                        && result2.Args[0]?.ToString() == "default"
                        && (int)result2.Args[1]! == 99;
                })
                .Add("DefaultValueMatcher_NonOptional_ShouldFail", () =>
                {
                    var builder = new ParameterBuilder();
                    var parameters = new List<IParameterDescriptor>
                    {
                        new TestParamDescriptor("a", typeof(string))
                    };
                    var result2 = builder.Build(parameters, null);
                    return !result2.IsSuccess;
                })
                .Add("TypeConverter_StringToInt_ShouldSucceed", () =>
                {
                    var result2 = TypeConverter.Convert("42", typeof(int));
                    return result2.converted != null
                        && (int)result2.converted == 42
                        && result2.error == null;
                })
                .Add("TypeConverter_StringToDouble_ShouldSucceed", () =>
                {
                    var result2 = TypeConverter.Convert("3.14", typeof(double));
                    return result2.converted != null
                        && Math.Abs((double)result2.converted - 3.14) < 0.001
                        && result2.error == null;
                })
                .Add("TypeConverter_StringToBool_ShouldSucceed", () =>
                {
                    var result2 = TypeConverter.Convert("true", typeof(bool));
                    return result2.converted != null
                        && (bool)result2.converted == true
                        && result2.error == null;
                })
                .Add("TypeConverter_InvalidInt_ShouldFail", () =>
                {
                    var result2 = TypeConverter.Convert("notANumber", typeof(int));
                    return result2.converted == null
                        && result2.error != null;
                })
                .Add("TypeConverter_NullToNullable_ShouldSucceed", () =>
                {
                    var result2 = TypeConverter.Convert(null, typeof(int?));
                    return result2.converted == null
                        && result2.error == null;
                })
                .Add("TypeConverter_NullToNonNullable_ShouldFail", () =>
                {
                    var result2 = TypeConverter.Convert(null, typeof(int));
                    return result2.converted == null
                        && result2.error != null;
                })
                .Add("TypeConverter_EnumParsing_ShouldSucceed", () =>
                {
                    var result2 = TypeConverter.Convert("ReadWrite", typeof(FileAccess));
                    return result2.converted != null
                        && (FileAccess)result2.converted == FileAccess.ReadWrite
                        && result2.error == null;
                })
                .Run();
            Assert.IsTrue(result.AllPassed, string.Join(", ", result.Messages));
        }

        private class TestParamDescriptor : IParameterDescriptor
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
}
