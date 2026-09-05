using System;
using System.Configuration;
using System.IO;
using Microsoft.Extensions.Configuration;
using PathManager;

namespace UniversalWorkflowFramework.Core.Utils
{
    public static class ConfigurationHelper
    {
        private static IConfiguration _configuration;

        static ConfigurationHelper()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(ProjectDirectories.ProjectRoot)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public static string GetConfiguration(string key, string defaultValue = "")
        {
            return _configuration[key] ?? defaultValue;
        }

        public static T GetConfiguration<T>(string key, T defaultValue = default)
        {
            try
            {
                var value = _configuration[key];
                if (value == null)
                {
                    return defaultValue;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
    }


}