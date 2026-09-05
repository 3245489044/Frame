
namespace TestInfrastructure
{

    public interface IFixtureContext
    {
        void Reset();
    }

    public abstract class FixtureContextBase : IFixtureContext
    {
        public virtual void Reset()
        {
        }
    }


    public interface IFixtureScope : IDisposable
    {
        void Enter();

        void Exit();
    }

    public abstract class FixtureScopeBase : IFixtureScope
    {
        private bool _isEntered;

        public virtual void Enter()
        {
            _isEntered = true;
        }

        public abstract void Exit();

        public void Dispose()
        {
            if (_isEntered)
            {
                Exit();
                _isEntered = false;
            }
        }
    }


    public class TestResult
    {
        public string Name { get; set; } = string.Empty;

        public bool Passed { get; set; }

        public string? ErrorMessage { get; set; }

        public TimeSpan Duration { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public string? Category { get; set; }

        public Dictionary<string, object> Data { get; set; } = new();
    }

    public class TestRunSummary
    {
        public string Name { get; set; } = string.Empty;

        public List<TestResult> Results { get; set; } = new();

        public int Total => Results.Count;

        public int Passed => Results.Count(r => r.Passed);

        public int Failed => Results.Count(r => !r.Passed);

        public double PassRate => Total > 0 ? (double)Passed / Total * 100 : 0;

        public TimeSpan TotalDuration { get; set; }

        public bool AllPassed => Failed == 0;
    }


    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class TestGroupAttribute : Attribute
    {
        public string GroupName { get; }

        public int Priority { get; set; } = 0;

        public string? Description { get; set; }

        public TestGroupAttribute(string groupName)
        {
            GroupName = groupName;
        }
    }
}
