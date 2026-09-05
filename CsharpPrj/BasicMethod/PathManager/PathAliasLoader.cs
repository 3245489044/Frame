
using System;

namespace PathManager
{
    public static class PathAliasLoader
    {
        public static int LoadFromEnvironment(string prefix = "FILEDRIVEN_ALIAS_", Action<string, object?>? logAction = null)
        {
            var envVars = Environment.GetEnvironmentVariables();
            int aliasCount = 0;

            foreach (var key in envVars.Keys)
            {
                var keyStr = key?.ToString();
                if (string.IsNullOrEmpty(keyStr) ||
                    !keyStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var alias = keyStr[prefix.Length..];
                var path = envVars[key]?.ToString();

                if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(path))
                {
                    PathResolver.RegisterAlias(alias, path);
                    logAction?.Invoke("Executor_LoadPathAliasesFromEnvironment_registered",
                        new { alias, path });
                    aliasCount++;
                }
            }

            if (aliasCount > 0)
                logAction?.Invoke("Executor_LoadPathAliasesFromEnvironment_count",
                    new { aliasCount });

            return aliasCount;
        }
    }
}
