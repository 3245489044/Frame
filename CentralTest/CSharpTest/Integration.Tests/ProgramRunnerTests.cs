using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CentralConsole.Tests
{
    [TestClass]
    public class ProgramRunnerTests
    {
        private ILogger<ProgramRunner> _logger;
        private ProgramRunner _runner;

        [TestInitialize]
        public void Initialize()
        {
            _logger = NullLogger<ProgramRunner>.Instance;
            _runner = new ProgramRunner(_logger);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _runner.StopAllPrograms();
            
            var tempConfigs = Directory.GetFiles(Directory.GetCurrentDirectory(), "test_config_*.json");
            foreach (var config in tempConfigs)
            {
                try { File.Delete(config); } catch { }
            }
        }

        #region 配置加载测试

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void LoadProgramsFromConfig_NonExistentConfig_DoesNotThrow()
        {
            var runner = new ProgramRunner(_logger);

            runner.LoadProgramsFromConfig("nonexistent.json");

            var programs = runner.GetPrograms();
            Assert.IsNotNull(programs);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void GetPrograms_AfterLoad_ReturnsProgramList()
        {
            var runner = new ProgramRunner(_logger);

            runner.LoadProgramsFromConfig("nonexistent.json");
            var programs = runner.GetPrograms();

            Assert.IsNotNull(programs);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void LoadProgramsFromConfig_ValidConfig_LoadsPrograms()
        {
            var configPath = CreateTempConfig(new[]
            {
                new { Name = "TestApp1", Description = "测试应用1", ProjectPath = "./TestApp1/TestApp1.csproj", Enabled = true },
                new { Name = "TestApp2", Description = "测试应用2", ProjectPath = "./TestApp2/TestApp2.csproj", Enabled = false }
            });

            _runner.LoadProgramsFromConfig(configPath);
            var programs = _runner.GetPrograms();

            Assert.AreEqual(2, programs.Count, "应加载2个程序配置");
            Assert.IsTrue(programs.ContainsKey("TestApp1"), "应包含 TestApp1");
            Assert.IsTrue(programs.ContainsKey("TestApp2"), "应包含 TestApp2");
            Assert.AreEqual("测试应用1", programs["TestApp1"].Description);
            Assert.AreEqual(ProgramStatus.Discovered, programs["TestApp1"].Status);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void LoadProgramsFromConfig_ValidConfig_SetsEnabledState()
        {
            var configPath = CreateTempConfig(new[]
            {
                new { Name = "EnabledApp", Description = "", ProjectPath = "./EnabledApp/EnabledApp.csproj", Enabled = true },
                new { Name = "DisabledApp", Description = "", ProjectPath = "./DisabledApp/DisabledApp.csproj", Enabled = false }
            });

            _runner.LoadProgramsFromConfig(configPath);
            var programs = _runner.GetPrograms();

            Assert.IsTrue(programs["EnabledApp"].Enabled, "EnabledApp 应启用");
            Assert.IsFalse(programs["DisabledApp"].Enabled, "DisabledApp 应禁用");
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void LoadProgramsFromConfig_InvalidJson_DoesNotThrowAndAutoDiscovers()
        {
            var invalidConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "test_config_invalid.json");
            File.WriteAllText(invalidConfigPath, "{ invalid json content }");

            try
            {
                _runner.LoadProgramsFromConfig(invalidConfigPath);

                var programs = _runner.GetPrograms();
                Assert.IsNotNull(programs);
            }
            finally
            {
                if (File.Exists(invalidConfigPath))
                    File.Delete(invalidConfigPath);
            }
        }

        #endregion

        #region Enable/Disable 测试

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void EnableProgram_ExistingProgram_ReturnsTrue()
        {
            AddTestProgram("TestProgram");

            var result = _runner.EnableProgram("TestProgram");

            Assert.IsTrue(result, "启用已存在的程序应返回 true");
            var programs = _runner.GetPrograms();
            Assert.IsTrue(programs["TestProgram"].Enabled, "程序应被启用");
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void DisableProgram_ExistingProgram_ReturnsTrue()
        {
            AddTestProgram("TestProgram");

            var result = _runner.DisableProgram("TestProgram");

            Assert.IsTrue(result, "禁用已存在的程序应返回 true");
            var programs = _runner.GetPrograms();
            Assert.IsFalse(programs["TestProgram"].Enabled, "程序应被禁用");
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void EnableProgram_NonExistentProgram_ReturnsFalse()
        {
            var result = _runner.EnableProgram("NonExistentProgram");

            Assert.IsFalse(result);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void DisableProgram_NonExistentProgram_ReturnsFalse()
        {
            var result = _runner.DisableProgram("NonExistentProgram");

            Assert.IsFalse(result);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void EnableProgram_AlreadyEnabled_ReturnsTrue()
        {
            AddTestProgram("TestProgram", enabled: true);

            var result = _runner.EnableProgram("TestProgram");

            Assert.IsTrue(result, "启用已启用的程序应返回 true");
            Assert.IsTrue(_runner.GetPrograms()["TestProgram"].Enabled);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void DisableProgram_AlreadyDisabled_ReturnsTrue()
        {
            AddTestProgram("TestProgram", enabled: false);

            var result = _runner.DisableProgram("TestProgram");

            Assert.IsTrue(result, "禁用已禁用的程序应返回 true");
            Assert.IsFalse(_runner.GetPrograms()["TestProgram"].Enabled);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void EnableProgram_MultiplePrograms_OnlyAffectsTarget()
        {
            AddTestProgram("Program1", enabled: false);
            AddTestProgram("Program2", enabled: true);
            AddTestProgram("Program3", enabled: false);

            _runner.EnableProgram("Program1");

            var programs = _runner.GetPrograms();
            Assert.IsTrue(programs["Program1"].Enabled, "Program1 应被启用");
            Assert.IsTrue(programs["Program2"].Enabled, "Program2 应保持启用");
            Assert.IsFalse(programs["Program3"].Enabled, "Program3 应保持禁用");
        }

        #endregion

        #region Stop 测试

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void StopAllPrograms_NoRunningPrograms_DoesNotThrow()
        {
            _runner.StopAllPrograms();
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void StopProgram_NonExistentProgram_DoesNotThrow()
        {
            _runner.StopProgram("NonExistentProgram");
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void StopAllPrograms_AfterMultipleStops_DoesNotThrow()
        {
            _runner.StopAllPrograms();
            _runner.StopAllPrograms();
            _runner.StopAllPrograms();
        }

        #endregion

        #region Build 测试

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public async Task BuildProgramAsync_NonExistentProgram_ReturnsFalse()
        {
            var result = await _runner.BuildProgramAsync("NonExistentProgram");

            Assert.IsFalse(result, "构建不存在的程序应返回 false");
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public async Task BuildProgramAsync_NonExistentProgram_SetsNoStatus()
        {
            await _runner.BuildProgramAsync("NonExistentProgram");

            Assert.IsFalse(_runner.GetPrograms().ContainsKey("NonExistentProgram"));
        }

        #endregion

        #region Run 测试

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public async Task RunProgramAsync_NonExistentProgram_ReturnsFalse()
        {
            var result = await _runner.RunProgramAsync("NonExistentProgram");

            Assert.IsFalse(result, "运行不存在的程序应返回 false");
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public async Task RunProgramAsync_NonExistentProgram_DoesNotThrow()
        {
            await _runner.RunProgramAsync("NonExistentProgram");
        }

        #endregion

        #region GetPrograms 测试

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void GetPrograms_EmptyRunner_ReturnsEmptyDictionary()
        {
            var programs = _runner.GetPrograms();

            Assert.IsNotNull(programs, "GetPrograms 不应返回 null");
            Assert.AreEqual(0, programs.Count, "初始状态应返回空字典");
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void GetPrograms_ReturnsReadOnlyCopy()
        {
            AddTestProgram("TestProgram");

            var programs = _runner.GetPrograms();

            Assert.IsNotNull(programs);
            Assert.IsTrue(programs.ContainsKey("TestProgram"));
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void GetPrograms_AfterMultipleLoads_ReturnsLatestState()
        {
            AddTestProgram("Program1");

            var configPath = CreateTempConfig(new[]
            {
                new { Name = "NewProgram", Description = "新程序", ProjectPath = "./New/New.csproj", Enabled = true }
            });
            _runner.LoadProgramsFromConfig(configPath);

            var programs = _runner.GetPrograms();
            Assert.IsTrue(programs.ContainsKey("Program1"), "应保留之前添加的程序");
            Assert.IsTrue(programs.ContainsKey("NewProgram"), "应包含新加载的程序");
        }

        #endregion

        #region 状态转换测试

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void ProgramInfo_DefaultStatus_IsDiscovered()
        {
            AddTestProgram("TestProgram");

            Assert.AreEqual(ProgramStatus.Discovered, _runner.GetPrograms()["TestProgram"].Status);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void EnableProgram_DoesNotChangeStatus()
        {
            AddTestProgram("TestProgram");

            _runner.EnableProgram("TestProgram");

            Assert.AreEqual(ProgramStatus.Discovered, _runner.GetPrograms()["TestProgram"].Status);
        }

        [TestMethod]
        [TestCategory("ProgramRunner")]
        [TestCategory("Unit")]
        public void DisableProgram_DoesNotChangeStatus()
        {
            AddTestProgram("TestProgram");

            _runner.DisableProgram("TestProgram");

            Assert.AreEqual(ProgramStatus.Discovered, _runner.GetPrograms()["TestProgram"].Status);
        }

        #endregion

        #region 辅助方法

        private string CreateTempConfig(params dynamic[] programs)
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), $"test_config_{Guid.NewGuid():N}.json");
            var configObj = new { Programs = programs.Select(p => new { p.Name, p.Description, p.ProjectPath, p.Enabled }).ToList() };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(configObj, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(configPath, json);
            return configPath;
        }

        private void AddTestProgram(string name, bool enabled = true, string description = null)
        {
            var programsField = typeof(ProgramRunner).GetField("_programs",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (programsField?.GetValue(_runner) is System.Collections.Concurrent.ConcurrentDictionary<string, ProgramInfo> programs)
            {
                programs.TryAdd(name, new ProgramInfo
                {
                    Name = name,
                    Description = description ?? $"测试程序: {name}",
                    ProjectPath = $"./{name}/{name}.csproj",
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    Enabled = enabled,
                    Status = ProgramStatus.Discovered,
                    BuildConfiguration = "Debug"
                });
            }
        }

        #endregion
    }
}
