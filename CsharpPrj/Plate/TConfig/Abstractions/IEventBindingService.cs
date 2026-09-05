
using System.Threading.Tasks;

namespace TConfig
{
    public interface IEventBindingService
    {
        Task<BindingResult> BindControlAsync(object control, EventConfig eventConfig);

        Task<List<BindingResult>> BindControlsAsync(
            IEnumerable<object> controls,
            IEnumerable<EventConfig> eventConfigs);
    }
}
