
using System.Threading.Tasks;

namespace TConfig
{
    public interface IUISubsystemInitializer
    {
        Task<bool> InitializeAsync(FrameworkContext context);

        string SubsystemName { get; }
    }
}
