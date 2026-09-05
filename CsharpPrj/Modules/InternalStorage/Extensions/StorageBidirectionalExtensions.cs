
using System;
using System.Globalization;
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Extensions;

public static class StorageBidirectionalExtensions
{
    private const string ReversePrefix = "bidir_reverse:";


    public static void MapForward<TKey, TValue>(
        this IStorageCell cell,
        string mapName,
        TKey key,
        TValue value)
        where TKey : notnull
        where TValue : notnull
    {
        var forwardKey = $"{mapName}:{key}";
        var reverseKey = $"{ReversePrefix}{mapName}:{KeyToString(value)}";

        cell.Set(forwardKey, value, StorageLayer.Session);
        cell.Set(reverseKey, key, StorageLayer.Session);
    }

    public static TValue? GetForward<TKey, TValue>(
        this IStorageCell cell,
        string mapName,
        TKey key)
        where TKey : notnull
        where TValue : notnull
    {
        var forwardKey = $"{mapName}:{key}";
        return cell.Get<TValue>(forwardKey, StorageLayer.Session);
    }


    public static TKey? GetBackward<TKey, TValue>(
        this IStorageCell cell,
        string mapName,
        TValue value)
        where TKey : notnull
        where TValue : notnull
    {
        var reverseKey = $"{ReversePrefix}{mapName}:{KeyToString(value)}";
        return cell.Get<TKey>(reverseKey, StorageLayer.Session);
    }


    public static void Unmap<TKey, TValue>(
        this IStorageCell cell,
        string mapName,
        TKey key)
        where TKey : notnull
        where TValue : notnull
    {
        var forwardKey = $"{mapName}:{key}";

        var value = cell.Get<TValue>(forwardKey, StorageLayer.Session);

        cell.Remove(forwardKey, StorageLayer.Session);

        if (value != null)
        {
            var reverseKey = $"{ReversePrefix}{mapName}:{KeyToString(value)}";
            cell.Remove(reverseKey, StorageLayer.Session);
        }
    }

    public static bool ContainsForward<TKey, TValue>(
        this IStorageCell cell,
        string mapName,
        TKey key)
        where TKey : notnull
        where TValue : notnull
    {
        var forwardKey = $"{mapName}:{key}";
        return cell.Contains(forwardKey, StorageLayer.Session);
    }

    public static bool ContainsBackward<TKey, TValue>(
        this IStorageCell cell,
        string mapName,
        TValue value)
        where TKey : notnull
        where TValue : notnull
    {
        var reverseKey = $"{ReversePrefix}{mapName}:{KeyToString(value)}";
        return cell.Contains(reverseKey, StorageLayer.Session);
    }

    private static string KeyToString<T>(T value) where T : notnull
    {
        return value switch
        {
            string s => s,
            bool b => b ? "1" : "0",
            char c => c.ToString(CultureInfo.InvariantCulture),
            byte b => b.ToString(CultureInfo.InvariantCulture),
            sbyte b => b.ToString(CultureInfo.InvariantCulture),
            short s => s.ToString(CultureInfo.InvariantCulture),
            ushort s => s.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint u => u.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            ulong u => u.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            Guid g => g.ToString("N"),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            // 禁用 GetHashCode 兜底：哈希作反向键不稳定（.NET Core 下 string 哈希每进程随机）且会碰撞，
            // 对不受支持的类型直接显式失败，避免静默写坏反向映射。
            _ => throw new NotSupportedException(
                $"StorageBidirectionalExtensions 不支持类型 '{typeof(T).FullName}' 作反向映射键。" +
                "请改用受支持的原语类型，或传入稳定序列化后的字符串键。")
        };
    }
}
