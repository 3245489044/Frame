#nullable enable
using UniversalWorkflowFramework.Core.Managers;

namespace UniversalWorkflowFramework.Core.Interfaces
{
    public static class UniversalReferenceManagerFactory
    {
        private static string? _dataDirectory;

        public static void SetDataDirectory(string directory)
        {
            _dataDirectory = directory;
        }

        public static IUniversalReferenceManager Create()
        {
            if (!string.IsNullOrEmpty(_dataDirectory))
            {
                return new PersistentUniversalReferenceManager(_dataDirectory);
            }
            return new DefaultUniversalReferenceManager();
        }

        public static IUniversalReferenceManager CreatePersistent(string dataDirectory)
        {
            return new PersistentUniversalReferenceManager(dataDirectory);
        }
    }
}