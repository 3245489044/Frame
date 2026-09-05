using System;

namespace DataManage.Exceptions
{
    public class StorageException : Exception
    {
        public string? StorageId { get; }

        public StorageException(string message, string? storageId = null, Exception? inner = null)
            : base(message, inner) => StorageId = storageId;
    }

    public class DuplicateKeyException : StorageException
    {
        public string Container { get; }
        public string KeyField { get; }
        public object? KeyValue { get; }

        public DuplicateKeyException(string container, string keyField, object? keyValue, string? storageId = null, Exception? inner = null)
            : base($"唯一键冲突：容器 '{container}' 中字段 '{keyField}' 的值 '{keyValue}' 已存在。", storageId, inner)
            => (Container, KeyField, KeyValue) = (container, keyField, keyValue);
    }

    public class DataNotFoundException : StorageException
    {
        public string? Key { get; }

        public DataNotFoundException(string message, string? storageId = null, string? key = null, Exception? inner = null)
            : base(message, storageId, inner) => Key = key;
    }

    public class UnsupportedStorageTypeException : StorageException
    {
        public Type? RequestedType { get; }

        public UnsupportedStorageTypeException(string message, string? storageId = null, Type? requestedType = null, Exception? inner = null)
            : base(message, storageId, inner) => RequestedType = requestedType;
    }

    public class StorageInitializationException : StorageException
    {
        public StorageInitializationException(string message, string? storageId = null, Exception? inner = null)
            : base(message, storageId, inner) { }
    }
}