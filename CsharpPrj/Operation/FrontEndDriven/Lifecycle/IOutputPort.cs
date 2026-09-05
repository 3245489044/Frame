
#nullable enable

using ECS.Core;

namespace Ports;

public interface IControlCreationOutput
{
    void CreateControl(Entity entity, string controlType);

    void AddToParent(Entity childEntity, Entity parentEntity);

    void RegisterMapping(Entity entity, string controlName, object control);
}

public interface IEventSubscriptionOutput
{
    void SubscribeSplitterMoved(object control, Entity entity);

    void UnsubscribeSplitterMoved(object control);
}

public interface IEventBindingOutput
{
    void BindEvent(object control, string eventName, string functionName, object bindingData);
}
