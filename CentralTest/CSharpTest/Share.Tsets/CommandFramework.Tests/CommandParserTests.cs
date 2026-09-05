using TestInfrastructure;
using CommandFramework.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class CommandParserTests : TestBase
    {
        [TestMethod]
        public void RunAll_Parse()
        {
            var result = new TestGroupBuilder("Parse")
                .Add("Parse_SimpleDoClick_ShouldSucceed", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do click addButton");
                    return parsed.IsSuccess
                        && parsed.Verb == "do"
                        && parsed.Action == "click"
                        && parsed.Target == "addButton";
                })
                .Add("Parse_DoSetWithValue_ShouldSucceed", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do set username value=admin");
                    return parsed.IsSuccess
                        && parsed.Verb == "do"
                        && parsed.Action == "set"
                        && parsed.Target == "username"
                        && parsed.Parameters != null
                        && parsed.Parameters["value"].ToString() == "admin";
                })
                .Add("Parse_EmptyString_ShouldFail", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("");
                    return !parsed.IsSuccess;
                })
                .Add("Parse_WhitespaceString_ShouldFail", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("   ");
                    return !parsed.IsSuccess;
                })
                .Add("Parse_NonDoVerbLook_ShouldSucceed", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("look window");
                    return parsed.IsSuccess
                        && parsed.Verb == "look"
                        && parsed.Action == null
                        && parsed.Target == "window";
                })
                .Add("Parse_NonDoVerbWait_ShouldSucceed", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("wait 2000");
                    return parsed.IsSuccess
                        && parsed.Verb == "wait"
                        && parsed.Action == null
                        && parsed.Target == "2000";
                })
                .Add("Parse_DoClickWithParams_ShouldSucceed", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do click addButton modifier=shift");
                    return parsed.IsSuccess
                        && parsed.Verb == "do"
                        && parsed.Action == "click"
                        && parsed.Target == "addButton"
                        && parsed.Parameters != null
                        && parsed.Parameters["modifier"].ToString() == "shift";
                })
                .Add("Parse_CommentLineHash_ShouldReturnNonEvent", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("# this is a comment");
                    return parsed.IsSuccess && parsed.IsNonEventCommand && parsed.NormalizedCommand == null;
                })
                .Add("Parse_CommentLineSlash_ShouldReturnNonEvent", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("// this is a comment");
                    return parsed.IsSuccess && parsed.IsNonEventCommand && parsed.NormalizedCommand == null;
                })
                .Add("Parse_QuotedParam_ShouldHandleEscape", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do set name value=\\\"quoted\\\"");
                    return parsed.IsSuccess
                        && parsed.Parameters != null
                        && parsed.Parameters["value"].ToString() == "\"quoted\"";
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Normalize()
        {
            var result = new TestGroupBuilder("Normalize")
                .Add("Normalize_Click_ShouldExpandToDoClick", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("click addButton");
                    return normalized == "do click addButton";
                })
                .Add("Normalize_Set_ShouldExpandToDoSet", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("set username admin");
                    return normalized == "do set username admin";
                })
                .Add("Normalize_Type_ShouldExpandToDoType", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("type inputBox hello");
                    return normalized == "do type inputBox hello";
                })
                .Add("Normalize_Select_ShouldExpandToDoSelect", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("select option1");
                    return normalized == "do select option1";
                })
                .Add("Normalize_Check_ShouldExpandToDoCheck", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("check cb1");
                    return normalized == "do check cb1";
                })
                .Add("Normalize_Uncheck_ShouldExpandToDoUncheck", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("uncheck cb1");
                    return normalized == "do uncheck cb1";
                })
                .Add("Normalize_AlreadyFull_ShouldRemainUnchanged", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("do click addButton");
                    return normalized == "do click addButton";
                })
                .Add("Normalize_EmptyString_ShouldReturnNull", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("");
                    return normalized == null;
                })
                .Add("Normalize_CommentHash_ShouldReturnNull", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("# comment");
                    return normalized == null;
                })
                .Add("Normalize_CommentSlash_ShouldReturnNull", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("// comment");
                    return normalized == null;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_IsNonEventCommand()
        {
            var result = new TestGroupBuilder("IsNonEventCommand")
                .Add("IsNonEventCommand_Load_ShouldBeTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("do", "load");
                })
                .Add("IsNonEventCommand_Close_ShouldBeTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("do", "close");
                })
                .Add("IsNonEventCommand_Echo_ShouldBeTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("do", "echo");
                })
                .Add("IsNonEventCommand_Click_ShouldBeFalse", () =>
                {
                    var parser = new CommandParser();
                    return !parser.IsNonEventCommand("do", "click");
                })
                .Add("IsNonEventCommand_Set_ShouldBeFalse", () =>
                {
                    var parser = new CommandParser();
                    return !parser.IsNonEventCommand("do", "set");
                })
                .Add("IsNonEventCommand_LookVerb_ShouldBeTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("look", null);
                })
                .Add("IsNonEventCommand_WaitVerb_ShouldBeTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("wait", null);
                })
                .Add("IsNonEventCommand_EmptyVerb_ShouldBeFalse", () =>
                {
                    var parser = new CommandParser();
                    return !parser.IsNonEventCommand("", null);
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ParseAsJsonCommand()
        {
            var result = new TestGroupBuilder("ParseAsJsonCommand")
                .Add("ParseAsJsonCommand_DoClick_ShouldMapToDo", () =>
                {
                    var parser = new CommandParser();
                    var json = parser.ParseAsJsonCommand("do click addButton");
                    return json.Verb == JsonVerb.Do
                        && json.Action == "click"
                        && json.Target == "addButton";
                })
                .Add("ParseAsJsonCommand_Look_ShouldMapToLook", () =>
                {
                    var parser = new CommandParser();
                    var json = parser.ParseAsJsonCommand("look window");
                    return json.Verb == JsonVerb.Look;
                })
                .Add("ParseAsJsonCommand_Wait_ShouldMapToWait", () =>
                {
                    var parser = new CommandParser();
                    var json = parser.ParseAsJsonCommand("wait 1000");
                    return json.Verb == JsonVerb.Wait;
                })
                .Add("ParseAsJsonCommand_Assert_ShouldMapToAssert", () =>
                {
                    var parser = new CommandParser();
                    var json = parser.ParseAsJsonCommand("assert visible");
                    return json.Verb == JsonVerb.Assert;
                })
                .Add("ParseAsJsonCommand_InvalidCommand_ShouldThrow", () =>
                {
                    var parser = new CommandParser();
                    try
                    {
                        parser.ParseAsJsonCommand("");
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                })
                .Add("TryParseAsJsonCommand_Invalid_ShouldReturnNull", () =>
                {
                    var parser = new CommandParser();
                    var json = parser.TryParseAsJsonCommand("");
                    return json == null;
                })
                .Add("InferValue_True_ShouldReturnBool", () =>
                {
                    var result2 = JsonCommand.InferValue("true");
                    return result2 is true;
                })
                .Add("InferValue_False_ShouldReturnBool", () =>
                {
                    var result2 = JsonCommand.InferValue("false");
                    return result2 is false;
                })
                .Add("InferValue_Null_ShouldReturnNull", () =>
                {
                    var result2 = JsonCommand.InferValue("null");
                    return result2 == null;
                })
                .Add("InferValue_Number_ShouldReturnLong", () =>
                {
                    var result2 = JsonCommand.InferValue("42");
                    return result2 is long l && l == 42;
                })
                .Add("InferValue_Decimal_ShouldReturnDouble", () =>
                {
                    var result2 = JsonCommand.InferValue("3.14");
                    return result2 is double d && Math.Abs(d - 3.14) < 0.001;
                })
                .Add("InferValue_String_ShouldReturnString", () =>
                {
                    var result2 = JsonCommand.InferValue("hello");
                    return result2 is string s && s == "hello";
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_RegisterCustom()
        {
            var result = new TestGroupBuilder("RegisterCustom")
                .Add("RegisterNormalization_ShouldWork", () =>
                {
                    var parser = new CommandParser();
                    parser.RegisterNormalization("open", "do click");
                    var normalized = parser.Normalize("open fileButton");
                    return normalized == "do click fileButton";
                })
                .Add("RegisterNonEventCommand_ShouldWork", () =>
                {
                    var parser = new CommandParser();
                    parser.RegisterNonEventCommand("customCmd");
                    return parser.IsNonEventCommand("do", "customCmd");
                })
                .Run();

            result.AssertAllPassed();
        }
    }
}
