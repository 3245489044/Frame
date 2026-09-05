
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BasicMethod.Tests.UniversalReference;

[TestGroup("UniversalReference")]
public abstract class UniversalReferenceTestBase : TestBase
{
    protected string? TempDataDir { get; private set; }

    protected string? TempDbPath { get; private set; }

    private readonly List<string> _isolatedDirs = new();

    private readonly List<string> _isolatedFiles = new();

    [TestInitialize]
    public void BaseInitialize()
    {
        TestLogger.CurrentLevel = TestLogger.LogLevel.Info;
        TestLogger.ConsoleOutput = true;
        TestLogger.Info($"初始化 {GetType().Name}");
    }

    [TestCleanup]
    public void BaseCleanup()
    {
        if (TempDataDir != null)
        {
            CleanupHelper.TryDeleteDirectory(TempDataDir);
            TempDataDir = null;
        }
        if (TempDbPath != null)
        {
            CleanupHelper.TryDeleteFile(TempDbPath);
            TempDbPath = null;
        }

        foreach (var dir in _isolatedDirs)
        {
            CleanupHelper.TryDeleteDirectory(dir);
        }
        _isolatedDirs.Clear();

        foreach (var file in _isolatedFiles)
        {
            CleanupHelper.TryDeleteFile(file);
        }
        _isolatedFiles.Clear();
    }

    protected string CreateTempDir(string prefix)
    {
        TempDataDir = CleanupHelper.CreateTempDirectory(prefix);
        TestLogger.Info($"临时目录: {TempDataDir}");
        return TempDataDir;
    }

    protected string CreateTempDbPath(string? extension = ".db")
    {
        TempDbPath = CleanupHelper.GetTempFilePath(extension);
        TestLogger.Info($"临时数据库: {TempDbPath}");
        return TempDbPath;
    }

    protected string CreateIsolatedTempDir(string prefix)
    {
        var dir = CleanupHelper.CreateTempDirectory(prefix);
        _isolatedDirs.Add(dir);
        TestLogger.Info($"隔离临时目录: {dir}");
        return dir;
    }

    protected string CreateIsolatedTempFile(string? extension = ".tmp")
    {
        var file = CleanupHelper.GetTempFilePath(extension);
        _isolatedFiles.Add(file);
        TestLogger.Info($"隔离临时文件: {file}");
        return file;
    }
}
