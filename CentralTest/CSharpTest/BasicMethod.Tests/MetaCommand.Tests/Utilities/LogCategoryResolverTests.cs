using MetaCommand;
using MetaCommand.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Utilities
{
    [TestClass]
    public class LogCategoryResolverTests
    {
        [TestMethod]
        public void Resolve_SystemCategory_ReturnsSystem()
        {
            var category = LogCategoryResolver.Resolve("System");
            Assert.AreEqual(LogCategory.System, category);
        }

        [TestMethod]
        public void Resolve_WdUIRender_ReturnsWdUI_Render()
        {
            var category = LogCategoryResolver.Resolve("WdUI_Render");
            Assert.AreEqual(LogCategory.WdUI_Render, category);
        }

        [TestMethod]
        public void Resolve_FileDrivenGeneral_ReturnsFileDriven_General()
        {
            var category = LogCategoryResolver.Resolve("FileDriven_General");
            Assert.AreEqual(LogCategory.FileDriven_General, category);
        }

        [TestMethod]
        public void Resolve_UnknownCategory_ReturnsSystem()
        {
            var category = LogCategoryResolver.Resolve("UnknownCategory");
            Assert.AreEqual(LogCategory.System, category);
        }

        [TestMethod]
        public void Resolve_EmptyString_ReturnsSystem()
        {
            var category = LogCategoryResolver.Resolve("");
            Assert.AreEqual(LogCategory.System, category);
        }

        [TestMethod]
        public void Resolve_NullString_ReturnsSystem()
        {
            var category = LogCategoryResolver.Resolve(null!);
            Assert.AreEqual(LogCategory.System, category);
        }

        [TestMethod]
        public void Resolve_DotNotation_ResolvesCorrectly()
        {
            var category = LogCategoryResolver.Resolve("wdui.render");
            Assert.AreEqual(LogCategory.WdUI_Render, category);
        }

        [TestMethod]
        public void Resolve_ShortName_ResolvesCorrectly()
        {
            var category = LogCategoryResolver.Resolve("render");
            Assert.AreEqual(LogCategory.WdUI_Render, category);
        }

        [TestMethod]
        public void GetModuleName_WdUI_ReturnsWdUI()
        {
            var moduleName = LogCategoryResolver.GetModuleName(LogCategory.WdUI_Render);
            Assert.AreEqual("WdUI", moduleName);
        }

        [TestMethod]
        public void GetModuleName_FileDriven_ReturnsFileDriven()
        {
            var moduleName = LogCategoryResolver.GetModuleName(LogCategory.FileDriven_Command);
            Assert.AreEqual("FileDriven", moduleName);
        }

        [TestMethod]
        public void GetModuleName_System_ThrowsInvalidCastException()
        {
            Assert.ThrowsException<InvalidCastException>(() =>
                LogCategoryResolver.GetModuleName(LogCategory.System));
        }
    }
}
