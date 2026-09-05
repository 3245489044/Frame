using TestInfrastructure;
using CommandFramework.Abstractions;
using CommandFramework.Registration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class CommandRegistryTests : TestBase
    {
        [TestMethod]
        public void RunAll_Register()
        {
            var result = new TestGroupBuilder("Register")
                .Add("Register_Generic_ShouldBeRegistered", () =>
                {
                    var registry = new CommandRegistry();
                    registry.Register<TestCommand>();
                    return registry.IsRegistered(typeof(TestCommand).FullName!);
                })
                .Add("Register_Named_ShouldBeRegistered", () =>
                {
                    var registry = new CommandRegistry();
                    registry.Register<TestCommand>("myCommand");
                    return registry.IsRegistered("myCommand");
                })
                .Add("Register_Factory_ShouldBeRegistered", () =>
                {
                    var registry = new CommandRegistry();
                    registry.Register("factoryCmd", sp => new TestCommand());
                    return registry.IsRegistered("factoryCmd");
                })
                .Add("Register_Factory_Null_ShouldThrow", () =>
                {
                    var registry = new CommandRegistry();
                    try
                    {
                        registry.Register("nullFactory", null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                })
                .Add("Register_Instance_ShouldBeRegistered", () =>
                {
                    var registry = new CommandRegistry();
                    var cmd = new TestCommand();
                    registry.Register(cmd);
                    return registry.IsRegistered("TestCommand");
                })
                .Add("Register_Instance_Null_ShouldThrow", () =>
                {
                    var registry = new CommandRegistry();
                    try
                    {
                        registry.Register((ICommand)null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                })
                .Add("Register_Instance_EmptyName_ShouldThrow", () =>
                {
                    var registry = new CommandRegistry();
                    try
                    {
                        registry.Register(new EmptyNameCommand());
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Resolve()
        {
            var result = new TestGroupBuilder("Resolve")
                .Add("Resolve_Existing_ShouldReturnInstance", () =>
                {
                    var registry = new CommandRegistry();
                    registry.Register<TestCommand>("resolveMe");
                    var cmd = registry.Resolve("resolveMe");
                    return cmd != null && cmd.Name == "TestCommand";
                })
                .Add("Resolve_NonExisting_ShouldReturnNull", () =>
                {
                    var registry = new CommandRegistry();
                    var cmd = registry.Resolve("nonExisting");
                    return cmd == null;
                })
                .Add("Resolve_EmptyName_ShouldReturnNull", () =>
                {
                    var registry = new CommandRegistry();
                    var cmd = registry.Resolve("");
                    return cmd == null;
                })
                .Add("Resolve_NullName_ShouldReturnNull", () =>
                {
                    var registry = new CommandRegistry();
                    var cmd = registry.Resolve(null!);
                    return cmd == null;
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_BatchOperations()
        {
            var result = new TestGroupBuilder("BatchOperations")
                .Add("RegisterRange_ShouldRegisterAll", () =>
                {
                    var registry = new CommandRegistry();
                    var commands = new Dictionary<string, Func<IServiceProvider?, ICommand>>
                    {
                        ["cmd1"] = sp => new TestCommand(),
                        ["cmd2"] = sp => new TestCommand()
                    };
                    registry.RegisterRange(commands);
                    return registry.IsRegistered("cmd1")
                        && registry.IsRegistered("cmd2")
                        && registry.Count == 2;
                })
                .Add("Clear_ShouldRemoveAll", () =>
                {
                    var registry = new CommandRegistry();
                    registry.Register<TestCommand>("toClear");
                    registry.Clear();
                    return registry.Count == 0
                        && !registry.IsRegistered("toClear");
                })
                .Add("Count_ShouldReturnCorrectNumber", () =>
                {
                    var registry = new CommandRegistry();
                    registry.Register<TestCommand>("c1");
                    registry.Register<TestCommand>("c2");
                    registry.Register<TestCommand>("c3");
                    return registry.Count == 3;
                })
                .Add("GetRegisteredCommands_ShouldReturnAllNames", () =>
                {
                    var registry = new CommandRegistry();
                    registry.Register<TestCommand>("alpha");
                    registry.Register<TestCommand>("beta");
                    var names = registry.GetRegisteredCommands();
                    return names.Length == 2
                        && names.Contains("alpha")
                        && names.Contains("beta");
                })
                .Run();

            result.AssertAllPassed();
        }
    }


    public class TestCommand : ICommand
    {
        public string Name => "TestCommand";
        public string? Description => "A test command";
        public bool CanUndo => false;
        public Task ExecuteAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UndoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public class EmptyNameCommand : ICommand
    {
        public string Name => string.Empty;
        public string? Description => null;
        public bool CanUndo => false;
        public Task ExecuteAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UndoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
