#nullable enable
using UniversalWorkflowFramework.Core.Graph;
using UniversalWorkflowFramework.Core.Managers;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Core.Interfaces
{
    public static class UniversalReferenceManagerFactory
    {
        private static string? _dataDirectory;

        /// <summary>设置默认落盘目录；传 null/空串即重置回内存实现（原实现无重置入口，全局态不可撤销）。</summary>
        public static void SetDataDirectory(string? directory)
        {
            _dataDirectory = string.IsNullOrWhiteSpace(directory) ? null : directory;
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