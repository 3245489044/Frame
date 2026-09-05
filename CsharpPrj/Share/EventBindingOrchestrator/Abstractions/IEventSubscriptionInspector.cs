
using System.Windows.Forms;

namespace Share.EventBindingOrchestrator.Abstractions;

public interface IEventSubscriptionInspector
{
    bool? HasSubscribers(Control control, string eventName);
}