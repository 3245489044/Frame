using MetaCommand;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Models
{
    [TestClass]
    public class MetaCommandTests
    {
        [TestMethod]
        public void Constructor_DefaultValues_AreSetCorrectly()
        {
            var cmd = new MetaCommand();
            Assert.AreEqual(MetaCommand.CurrentVersion, cmd.Version);
            Assert.AreEqual(MetaVerb.Do, cmd.Verb);
            Assert.IsNull(cmd.Target);
            Assert.IsNull(cmd.Action);
            Assert.IsNull(cmd.Parameters);
            Assert.IsNull(cmd.OriginalCommand);
            Assert.AreNotEqual(Guid.Empty, cmd.CommandId);
        }

        [TestMethod]
        public void Parse_DelegatesToCommandParser()
        {
            var cmd = MetaCommand.Parse("do load file");
            Assert.IsNotNull(cmd);
            Assert.AreEqual(MetaVerb.Do, cmd.Verb);
            Assert.AreEqual("load", cmd.Action);
            Assert.AreEqual("file", cmd.Target);
        }

        [TestMethod]
        public void ValidateCommand_DoVerbWithoutAction_ReturnsInvalid()
        {
            var cmd = new MetaCommand { Verb = MetaVerb.Do, Target = "file" };
            var validationResult = cmd.ValidateCommand();
            Assert.IsFalse(validationResult.IsValid);
            Assert.IsTrue(validationResult.Errors.Exists(e => e.PropertyName == "Action"));
        }

        [TestMethod]
        public void ValidateCommand_DoVerbWithAction_ReturnsValid()
        {
            var cmd = new MetaCommand { Verb = MetaVerb.Do, Action = "load", Target = "file" };
            Assert.IsTrue(cmd.ValidateCommand().IsValid);
        }

        [TestMethod]
        public void ValidateCommand_GetVerbWithoutTarget_ReturnsInvalid()
        {
            var cmd = new MetaCommand { Verb = MetaVerb.Get };
            var validationResult = cmd.ValidateCommand();
            Assert.IsFalse(validationResult.IsValid);
            Assert.IsTrue(validationResult.Errors.Exists(e => e.PropertyName == "Target"));
        }

        [TestMethod]
        public void ValidateCommand_WaitVerbWithConditionInParameters_ReturnsValid()
        {
            var cmd = new MetaCommand
            {
                Verb = MetaVerb.Wait,
                Parameters = new Dictionary<string, object> { { "condition", "visible" } }
            };
            Assert.IsTrue(cmd.ValidateCommand().IsValid);
        }

        [TestMethod]
        public void ValidateCommand_AssertVerbWithTarget_ReturnsValid()
        {
            var cmd = new MetaCommand { Verb = MetaVerb.Assert, Target = "is_visible" };
            Assert.IsTrue(cmd.ValidateCommand().IsValid);
        }

        [TestMethod]
        public void ToDebugString_ReturnsFormattedString()
        {
            var cmd = new MetaCommand
            {
                Verb = MetaVerb.Do,
                Action = "load",
                Target = "file",
                Parameters = new Dictionary<string, object> { { "key", "value" } }
            };
            var debug = cmd.ToDebugString();
            Assert.IsTrue(debug.Contains(cmd.CommandId.ToString()));
            Assert.IsTrue(debug.Contains("Do"));
            Assert.IsTrue(debug.Contains("load"));
            Assert.IsTrue(debug.Contains("file"));
        }

        [TestMethod]
        public void MetaVerbExtensions_ToCommandString_ReturnsLowercase()
        {
            Assert.AreEqual("do", MetaVerb.Do.ToCommandString());
            Assert.AreEqual("get", MetaVerb.Get.ToCommandString());
            Assert.AreEqual("look", MetaVerb.Look.ToCommandString());
            Assert.AreEqual("wait", MetaVerb.Wait.ToCommandString());
            Assert.AreEqual("assert", MetaVerb.Assert.ToCommandString());
        }
    }
}
