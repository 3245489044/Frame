

namespace FrontEndDriven.ECS;

public interface IControlStrategy
{
    string ControlType { get; }

    object Create();
}
