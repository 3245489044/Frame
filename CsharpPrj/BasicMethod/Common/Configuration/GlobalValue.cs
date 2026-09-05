#nullable disable

namespace Common.GraphMindStudio.GlobalValue
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    public static class NestedDictionaryFramework
    {
        public static class FlowDictionaryOperations
        {
            public static Func<int, object, List<object>> CreateObjectList = (int count, object obj) =>
            {
                return Enumerable.Repeat(obj, count).ToList();
            };
        }

        public static class PathValueOperations
        {
            public static T GetPathGetValue<T>(Dictionary<string, object> dict, string path)
            {
                if (dict == null || string.IsNullOrEmpty(path))
                    return default;

                var keys = path.Split('.');
                object current = dict;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (current is Dictionary<string, object> d)
                    {
                        if (!d.TryGetValue(keys[i], out current))
                            return default;
                    }
                    else
                    {
                        return default;
                    }
                }
                return (T)current;
            }

            public static void GetPathSetValue(Dictionary<string, object> dict, string path, object value)
            {
                if (dict == null || string.IsNullOrEmpty(path))
                    return;

                var keys = path.Split('.');
                object current = dict;
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (current is Dictionary<string, object> d)
                    {
                        if (!d.TryGetValue(keys[i], out current))
                        {
                            var newDict = new Dictionary<string, object>();
                            d[keys[i]] = newDict;
                            current = newDict;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                if (current is Dictionary<string, object> target)
                {
                    target[keys[^1]] = value;
                }
            }
        }

        public static class StringOperations
        {
            public static string GetPathGetEntireString(Dictionary<string, object> dict, string path)
            {
                return PathValueOperations.GetPathGetValue<string>(dict, path);
            }
        }
    }


    public static class DictionaryProcessor
    {
        public class KeyPair
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }
        public class ProcessingResult
        {
            public object Result { get; set; }
            public string Error { get; set; }
            public bool Success { get; set; }
        }
        public class ProcessingContext
        {
            public ICollection<string> GetKeys() => new List<string>();
            public IEnumerable<string> GetChildKeys(string parentKey) => new List<string>();
            public void Register(string key, Func<KeyPair, object> processor, bool overwriteIfExists = true, bool handleHierarchy = true) { }
            public void RegisterAction(string key, Action<KeyPair> handler, bool overwriteIfExists = true, bool handleHierarchy = true) { }
            public void RegisterRange(IDictionary<string, Func<KeyPair, object>> newHandlers, bool overwriteIfExists = true, bool handleHierarchy = true) { }
            public bool Remove(string key) => false;
            public int RemoveWithChildren(string parentKey) => 0;
            public void Clear() { }
            public object Execute(string key, KeyPair keyPair) => null;
            public bool TryExecute(string key, KeyPair keyPair, out object result, out string error) { result = null; error = string.Empty; return false; }
            public IDictionary<string, ProcessingResult> ExecuteRange(IDictionary<string, KeyPair> data, bool continueOnError = false) => new Dictionary<string, ProcessingResult>();
        }

        public static void Register(ConcurrentDictionary<string, Func<KeyPair, object>> handlers, string key, Func<KeyPair, object> handler, bool overwriteIfExists = true, bool handleHierarchy = true)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            if (overwriteIfExists)
                handlers.AddOrUpdate(key, handler, (_, _) => handler);
            else if (!handlers.TryAdd(key, handler))
                throw new InvalidOperationException($"键 '{key}' 已存在且不允许覆盖");
        }

        public static void RegisterAction(ConcurrentDictionary<string, Func<KeyPair, object>> handlers, string key, Action<KeyPair> handler, bool overwriteIfExists = true, bool handleHierarchy = true)
            => Register(handlers, key, kp => { handler(kp); return null; }, overwriteIfExists, handleHierarchy);
    }

    public static class DataSet
    {
        public static GlobalVariableSet globalvariableset = new GlobalVariableSet();
    }

    public class GlobalVariableSet
    {
        public Dictionary<string, object> dataStorage { get; private set; } = new Dictionary<string, object>();
    }
}
