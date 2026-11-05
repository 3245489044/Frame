
using Modules.DiagnosticContext.Core;

namespace Modules.DiagnosticContext.Formatting;

public interface IContextFormatter
{
    string Format(ScopeNode scope);
}
