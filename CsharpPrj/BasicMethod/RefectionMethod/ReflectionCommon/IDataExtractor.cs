#nullable enable

using System.Collections.Generic;

namespace Modules.ReflectionCommon
{
    public interface IDataExtractor
    {
        Dictionary<string, object?> ExtractAllFields(object target, bool includePrivate = true);

        T? ExtractTypedValue<T>(object target, string path);

        Dictionary<string, object?> ExtractByPattern(object target, string pattern);
    }
}
