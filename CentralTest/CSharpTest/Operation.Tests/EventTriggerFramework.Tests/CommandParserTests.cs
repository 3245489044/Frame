using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class CommandParserTests : TestBase
    {
        [TestMethod]
        public void RunAll_Parse()
        {
            var result = RunTestGroup("CommandParser_Parse", builder =>
            {
                builder.Add("Parse_DoClick_ShouldReturnParsedCommand", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do click btnOK");
                    return parsed.IsSuccess
                        && parsed.Verb == "do"
                        && parsed.Action == "click"
                        && parsed.Target == "btnOK"
                        && !parsed.IsNonEventCommand;
                });

                builder.Add("Parse_DoSet_ShouldReturnParsedCommand", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do set txtName John");
                    return parsed.IsSuccess
                        && parsed.Verb == "do"
                        && parsed.Action == "set"
                        && parsed.Target == "txtName"
                        && parsed.Parameters != null
                        && parsed.Parameters.ContainsKey("John");
                });

                builder.Add("Parse_NonEventCommand_ShouldMarkIsNonEvent", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("echo hello");
                    return parsed.IsSuccess
                        && parsed.Verb == "echo"
                        && parsed.Action == "echo"
                        && parsed.Target == "hello"
                        && parsed.IsNonEventCommand;
                });

                builder.Add("Parse_EmptyString_ShouldReturnFailure", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("");
                    return !parsed.IsSuccess;
                });

                builder.Add("Parse_Whitespace_ShouldReturnFailure", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("   ");
                    return !parsed.IsSuccess;
                });

                builder.Add("Parse_Null_ShouldReturnFailure", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse(null!);
                    return !parsed.IsSuccess;
                });

                builder.Add("Parse_CommentLine_ShouldReturnEmpty", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("# this is a comment");
                    return parsed.IsSuccess && parsed.NormalizedCommand == null;
                });

                builder.Add("Parse_CommentLineDoubleSlash_ShouldReturnEmpty", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("// this is a comment");
                    return parsed.IsSuccess && parsed.NormalizedCommand == null;
                });

                builder.Add("Parse_WithParameters_ShouldParseKeyValue", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do click btnOK modifier=show");
                    return parsed.IsSuccess
                        && parsed.Parameters != null
                        && parsed.Parameters["modifier"] as string == "show";
                });

                builder.Add("Parse_WithParameters_ShouldParseFlag", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do click btnOK wait");
                    return parsed.IsSuccess
                        && parsed.Parameters != null
                        && (bool)parsed.Parameters["wait"] == true;
                });

                builder.Add("Parse_QuotedTarget_ShouldHandleQuotes", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("do set txtName \"John Doe\"");
                    return parsed.IsSuccess
                        && parsed.Target == "txtName"
                        && parsed.Parameters != null
                        && parsed.Parameters["John Doe"] != null;
                });

                builder.Add("Parse_NonDoVerb_ShouldSetActionToVerb", () =>
                {
                    var parser = new CommandParser();
                    var parsed = parser.Parse("look at something");
                    return parsed.IsSuccess
                        && parsed.Verb == "look"
                        && parsed.Action == "look"
                        && parsed.Target == "at"
                        && parsed.IsNonEventCommand;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Normalize()
        {
            var result = RunTestGroup("CommandParser_Normalize", builder =>
            {
                builder.Add("Normalize_Click_ShouldExpandToDoClick", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("click btnOK");
                    return normalized == "do click btnOK";
                });

                builder.Add("Normalize_Set_ShouldExpandToDoSet", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("set txtName John");
                    return normalized == "do set txtName John";
                });

                builder.Add("Normalize_Type_ShouldExpandToDoType", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("type hello");
                    return normalized == "do type hello";
                });

                builder.Add("Normalize_Select_ShouldExpandToDoSelect", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("select option1");
                    return normalized == "do select option1";
                });

                builder.Add("Normalize_Check_ShouldExpandToDoCheck", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("check chkBox");
                    return normalized == "do check chkBox";
                });

                builder.Add("Normalize_Uncheck_ShouldExpandToDoUncheck", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("uncheck chkBox");
                    return normalized == "do uncheck chkBox";
                });

                builder.Add("Normalize_AlreadyDo_ShouldNotChange", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("do click btnOK");
                    return normalized == "do click btnOK";
                });

                builder.Add("Normalize_Comment_ShouldReturnNull", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("# comment");
                    return normalized == null;
                });

                builder.Add("Normalize_Empty_ShouldReturnNull", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("");
                    return normalized == null;
                });

                builder.Add("Normalize_Whitespace_ShouldReturnNull", () =>
                {
                    var parser = new CommandParser();
                    var normalized = parser.Normalize("   ");
                    return normalized == null;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_IsNonEventCommand()
        {
            var result = RunTestGroup("CommandParser_IsNonEventCommand", builder =>
            {
                builder.Add("IsNonEventCommand_Echo_ShouldReturnTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("echo", "echo");
                });

                builder.Add("IsNonEventCommand_Sleep_ShouldReturnTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("sleep", "sleep");
                });

                builder.Add("IsNonEventCommand_Wait_ShouldReturnTrue", () =>
                {
                    var parser = new CommandParser();
                    return parser.IsNonEventCommand("wait", "wait");
                });

                builder.Add("IsNonEventCommand_DoClick_ShouldReturnFalse", () =>
                {
                    var parser = new CommandParser();
                    return !parser.IsNonEventCommand("do", "click");
                });

                builder.Add("IsNonEventCommand_DoSet_ShouldReturnFalse", () =>
                {
                    var parser = new CommandParser();
                    return !parser.IsNonEventCommand("do", "set");
                });

                builder.Add("IsNonEventCommand_EmptyVerb_ShouldReturnFalse", () =>
                {
                    var parser = new CommandParser();
                    return !parser.IsNonEventCommand("", null);
                });

                builder.Add("IsNonEventCommand_NullVerb_ShouldReturnFalse", () =>
                {
                    var parser = new CommandParser();
                    return !parser.IsNonEventCommand(null!, null);
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_RegisterNormalization()
        {
            var result = RunTestGroup("CommandParser_RegisterNormalization", builder =>
            {
                builder.Add("RegisterNormalization_ShouldAddNewRule", () =>
                {
                    var parser = new CommandParser();
                    parser.RegisterNormalization("custom", "do custom");
                    var normalized = parser.Normalize("custom arg1");
                    return normalized == "do custom arg1";
                });

                builder.Add("RegisterNormalization_ShouldOverrideExisting", () =>
                {
                    var parser = new CommandParser();
                    parser.RegisterNormalization("click", "do customClick");
                    var normalized = parser.Normalize("click btnOK");
                    return normalized == "do customClick btnOK";
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_RegisterNonEventCommand()
        {
            var result = RunTestGroup("CommandParser_RegisterNonEventCommand", builder =>
            {
                builder.Add("RegisterNonEventCommand_ShouldAddToNonEventList", () =>
                {
                    var parser = new CommandParser();
                    parser.RegisterNonEventCommand("customCmd");
                    return parser.IsNonEventCommand("customCmd", "customCmd");
                });

                builder.Add("RegisterNonEventCommand_DoCustom_ShouldBeNonEvent", () =>
                {
                    var parser = new CommandParser();
                    parser.RegisterNonEventCommand("customCmd");
                    return parser.IsNonEventCommand("do", "customCmd");
                });
            });

            result.AssertAllPassed();
        }
    }
}
