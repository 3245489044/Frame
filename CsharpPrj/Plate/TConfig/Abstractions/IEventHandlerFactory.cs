
using System;

namespace TConfig
{
    public interface IEventHandlerFactory
    {
        Delegate? CreateHandler(EventConfig eventConfig, IFunctionExecutor functionExecutor);

        Delegate? CreateHandler(EventConfig eventConfig, IFunctionExecutor functionExecutor, object control);
    }
}
