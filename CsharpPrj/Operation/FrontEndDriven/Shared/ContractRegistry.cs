using System.Threading;
using Common.PlaceholderFramework.SchemaValidation;
using FrontEndDriven.Shared;

namespace FrontEndDriven.Shared
{
    public static class ContractRegistry
    {
        public const string DefaultKey = "__default__";

        private static readonly SchemaRegistry<string> _registry = new(DefaultKey);

        private static readonly ReaderWriterLockSlim _rwLock = new();

        static ContractRegistry()
        {
            RegisterAll();
        }

        private static void RegisterAll()
        {
            Register(DefaultKey, ContractBuilder.DefaultContract);

            foreach (var kvp in ControlTypeRegistry.SchemaFactories)
            {
                Register(kvp.Key, kvp.Value());
            }
        }

        public static void Register(string controlType, JsonSchema contract)
        {
            _rwLock.EnterWriteLock();
            try
            {
                _registry.Register(controlType.ToLowerInvariant(), contract);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public static JsonSchema GetContract(string controlType)
        {
            _rwLock.EnterReadLock();
            try
            {
                return _registry.GetSchema(controlType?.ToLowerInvariant());
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }

        public static JsonSchema GetDefaultContract()
        {
            _rwLock.EnterReadLock();
            try
            {
                return _registry.GetDefaultSchema();
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }

        public static bool IsValidControlType(string controlType)
        {
            return HasContract(controlType);
        }

        public static bool HasContract(string controlType)
        {
            _rwLock.EnterReadLock();
            try
            {
                return _registry.HasSchema(controlType?.ToLowerInvariant());
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }

        public static IEnumerable<string> GetRegisteredTypes()
        {
            _rwLock.EnterReadLock();
            try
            {
                return _registry.GetRegisteredKeys().ToList();
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }

        public static void Clear()
        {
            _rwLock.EnterWriteLock();
            try
            {
                _registry.Clear();
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public static void Reset()
        {
            _rwLock.EnterWriteLock();
            try
            {
                _registry.Clear();
                RegisterAll();
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
    }
}