#nullable enable
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Core.Managers
{
    public class DefaultUniversalReferenceManager : ReferenceManagerBase
    {
        public override Task InitializeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
