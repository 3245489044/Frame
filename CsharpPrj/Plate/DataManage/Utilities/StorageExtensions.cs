using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage.Utilities
{
    public static class StorageExtensions
    {
        public static async Task SaveBatchAsync<T>(this IDataStorage storage, IEnumerable<KeyValuePair<string, T>> data)
        {
            foreach (var item in data)
            {
                await storage.SaveAsync(item.Key, item.Value);
            }
        }

        public static async Task<Dictionary<string, T?>> LoadBatchAsync<T>(this IDataStorage storage, IEnumerable<string> keys)
        {
            var result = new Dictionary<string, T?>();
            foreach (var key in keys)
            {
                var value = await storage.LoadAsync<T>(key);
                result[key] = value;
            }
            return result;
        }

        public static async Task DeleteBatchAsync(this IDataStorage storage, IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                await storage.DeleteAsync(key);
            }
        }

        public static async Task<T> SaveAndReturnAsync<T>(this IDataStorage storage, string key, T data)
        {
            await storage.SaveAsync(key, data);
            return data;
        }

        public static async Task<T> LoadOrCreateAsync<T>(this IDataStorage storage, string key, System.Func<T> createFunc)
            where T : class
        {
            var data = await storage.LoadAsync<T>(key);
            if (data == null)
            {
                data = createFunc();
                await storage.SaveAsync(key, data);
            }
            return data;
        }
    }
}