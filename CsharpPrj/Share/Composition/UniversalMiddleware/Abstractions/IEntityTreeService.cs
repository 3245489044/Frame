
using System.Windows.Forms;
using TConfig.ECS;

namespace UniversalMiddleware.Abstractions;

public interface IEntityTreeService
{
    Control? FindControlByName(Entity rootEntity, string name);

    IConfigContainer? FindContainerByName(Entity rootEntity, string name);

    IEnumerable<IConfigContainer> FindAllContainers(Entity rootEntity);
}
