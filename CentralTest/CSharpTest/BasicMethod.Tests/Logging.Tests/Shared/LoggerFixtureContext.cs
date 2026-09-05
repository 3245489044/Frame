
using CentralConsole.Testing.Abstractions;
using Common.Diagnostics;

namespace Logging.Tests.Shared
{
    public class LoggerFixtureContext : FixtureContextBase
    {
        public Common.Diagnostics.Logger Logger { get; set; } = null!;

        public string ModuleName { get; set; } = "TestModule";

        public string? CapturedConsoleOutput { get; set; }

        public string? CapturedDebugOutput { get; set; }

        public List<string> ReceivedUiMessages { get; set; } = new();

        public object? Result { get; set; }

        public override void Reset()
        {
            Logger = null!;
            ModuleName = "TestModule";
            CapturedConsoleOutput = null;
            CapturedDebugOutput = null;
            ReceivedUiMessages.Clear();
            Result = null;
        }
    }
}
