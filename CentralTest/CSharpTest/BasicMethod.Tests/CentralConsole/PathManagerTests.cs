using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CentralConsole.Tests
{
    [TestClass]
    public class PathManagerTests
    {
        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void GetFullPath_RelativePath_ReturnsAbsolutePath()
        {
            var relativePath = "./test.txt";

            var fullPath = PathManager.GetFullPath(relativePath);

            Assert.IsTrue(Path.IsPathFullyQualified(fullPath), "GetFullPath 应该返回绝对路径");
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void GetWorkingDirectory_NullWorkingDir_ReturnsProjectPath()
        {
            var projectPath = AppContext.BaseDirectory;
            var expectedDir = Path.GetDirectoryName(projectPath);

            var workingDir = PathManager.GetWorkingDirectory(projectPath, null);

            Assert.AreEqual(expectedDir, workingDir, "GetWorkingDirectory 应该返回项目路径当工作目录为 null 时");
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void ConfigFilePath_And_WorkspaceRoot_AreAccessible()
        {
            var configPath = PathManager.ConfigFilePath;
            var workspaceRoot = PathManager.WorkspaceRoot;

            Assert.IsFalse(string.IsNullOrEmpty(configPath), "ConfigFilePath 不应为空");
            Assert.IsFalse(string.IsNullOrEmpty(workspaceRoot), "WorkspaceRoot 不应为空");
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void GetFullPath_AbsolutePath_ReturnsSamePath()
        {
            var absolutePath = @"C:\test\file.txt";

            var result = PathManager.GetFullPath(absolutePath);

            Assert.AreEqual(absolutePath, result, "绝对路径应原样返回");
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GetFullPath_EmptyPath_ThrowsArgumentNullException()
        {
            PathManager.GetFullPath("");
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void IsValidPath_ValidPath_ReturnsTrue()
        {
            var validPath = @"C:\valid\path";

            var result = PathManager.IsValidPath(validPath);

            Assert.IsTrue(result);
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void IsValidPath_InvalidPath_ReturnsFalse()
        {
            Assert.IsFalse(PathManager.IsValidPath(""));
            Assert.IsFalse(PathManager.IsValidPath(null));
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void EnsureTrailingSlash_PathWithoutSlash_AddsSlash()
        {
            var path = @"C:\test";

            var result = PathManager.EnsureTrailingSlash(path);

            Assert.IsTrue(result.EndsWith(Path.DirectorySeparatorChar.ToString()));
        }

        [TestMethod]
        [TestCategory("PathManager")]
        [TestCategory("Unit")]
        public void EnsureTrailingSlash_PathWithSlash_ReturnsSame()
        {
            var path = @"C:\test\";

            var result = PathManager.EnsureTrailingSlash(path);

            Assert.AreEqual(path, result);
        }
    }
}
