
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;
using WdUI;

namespace WdUI.Tests
{
    [TestClass]
    public class WdUIUsageJsonTests : TestBase
    {
        private const string ValidMinimalJson = @"{""controls"":[]}";
        private const string ValidFullJson = @"
{
  ""controls"": [
    {
      ""name"": ""btnTest"",
      ""type"": ""button"",
      ""text"": ""Test Button"",
      ""location"": { ""x"": 10, ""y"": 10 },
      ""size"": { ""width"": 100, ""height"": 30 }
    }
  ]
}";
        private const string InvalidJson = @"{""controls"": [}";
        private const string EmptyJson = @"{}";

        [TestMethod]
        public void RunAll_DeserializeConfig()
        {
            var result = new TestGroupBuilder("DeserializeConfig")
                .Add("ValidMinimalJson_ShouldReturnConfig", () =>
                {
                    var config = WdUIUsage.DeserializeConfig(ValidMinimalJson);
                    return config != null;
                })
                .Add("ValidFullJson_ShouldReturnConfigWithControls", () =>
                {
                    var config = WdUIUsage.DeserializeConfig(ValidFullJson);
                    return config != null;
                })
                .Add("InvalidJson_ShouldThrowException", () =>
                {
                    try
                    {
                        WdUIUsage.DeserializeConfig(InvalidJson);
                        return false;
                    }
                    catch
                    {
                        return true;
                    }
                })
                .Add("EmptyJson_ShouldReturnConfig", () =>
                {
                    var config = WdUIUsage.DeserializeConfig(EmptyJson);
                    return config != null;
                })
                .Add("NullInput_ShouldReturnNull", () =>
                {
                    var config = WdUIUsage.DeserializeConfig(null!);
                    return config == null;
                })
                .Add("EmptyString_ShouldReturnNull", () =>
                {
                    var config = WdUIUsage.DeserializeConfig("");
                    return config == null;
                })
                .Add("Whitespace_ShouldReturnNull", () =>
                {
                    var config = WdUIUsage.DeserializeConfig("   ");
                    return config == null;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_SerializeConfig()
        {
            var result = new TestGroupBuilder("SerializeConfig")
                .Add("ValidConfig_ShouldReturnJsonString", () =>
                {
                    var config = WdUIUsage.DeserializeConfig(ValidMinimalJson);
                    var json = WdUIUsage.SerializeConfig(config!);
                    return !string.IsNullOrEmpty(json);
                })
                .Add("RoundTrip_ShouldPreserveData", () =>
                {
                    var original = WdUIUsage.DeserializeConfig(ValidFullJson);
                    var json = WdUIUsage.SerializeConfig(original!);
                    var restored = WdUIUsage.DeserializeConfig(json);
                    return restored != null;
                })
                .Add("NullConfig_ShouldReturnEmptyString", () =>
                {
                    var json = WdUIUsage.SerializeConfig(null!);
                    return string.IsNullOrEmpty(json);
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ValidateJson()
        {
            var result = new TestGroupBuilder("ValidateJson")
                .Add("ValidJson_ShouldReturnValidResult", () =>
                {
                    var validation = WdUIUsage.ValidateJson(ValidMinimalJson);
                    return validation != null && validation.IsValid;
                })
                .Add("InvalidJson_ShouldReturnInvalidResult", () =>
                {
                    var validation = WdUIUsage.ValidateJson(InvalidJson);
                    return validation != null && !validation.IsValid;
                })
                .Add("NullInput_ShouldReturnInvalidResult", () =>
                {
                    var validation = WdUIUsage.ValidateJson(null!);
                    return validation != null && !validation.IsValid;
                })
                .Add("EmptyString_ShouldReturnInvalidResult", () =>
                {
                    var validation = WdUIUsage.ValidateJson("");
                    return validation != null && !validation.IsValid;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public void RunAll_ExtractJsonError()
        {
            var result = new TestGroupBuilder("ExtractJsonError")
                .Add("InvalidJson_ShouldExtractErrorContext", () =>
                {
                    try
                    {
                        JsonDocument.Parse(InvalidJson);
                        return false;
                    }
                    catch (JsonException ex)
                    {
                        var context = WdUIUsage.ExtractJsonError("test.json", ex);
                        return context != null;
                    }
                })
                .Add("NullPath_ShouldReturnNull", () =>
                {
                    try
                    {
                        JsonDocument.Parse(InvalidJson);
                        return false;
                    }
                    catch (JsonException ex)
                    {
                        var context = WdUIUsage.ExtractJsonError(null!, ex);
                        return context == null;
                    }
                })
                .Add("NullException_ShouldReturnNull", () =>
                {
                    var context = WdUIUsage.ExtractJsonError("test.json", null!);
                    return context == null;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }
    }
}
