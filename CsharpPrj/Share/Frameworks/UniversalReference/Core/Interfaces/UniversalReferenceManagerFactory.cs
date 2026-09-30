#nullable enable
using UniversalWorkflowFramework.Core.Graph;
using UniversalWorkflowFramework.Core.Managers;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

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

        /// <summary>创建以 <see cref="IResourceReferenceStore"/> 端口（L1 存储原语层）为后端的管理器（B0 激活）。</summary>
        public static IUniversalReferenceManager CreateStoreBacked(IResourceReferenceStore store)
        {
            return new StoreBackedUniversalReferenceManager(store);
        }

        /// <summary>创建引用图引擎（B0 激活：图引擎可被装配的唯一入口）。</summary>
        public static ReferenceGraphEngine CreateGraphEngine(IUniversalReferenceManager manager)
        {
            return new ReferenceGraphEngine(manager);
        }
    }
}