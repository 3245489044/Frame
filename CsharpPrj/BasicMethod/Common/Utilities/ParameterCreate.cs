namespace Common.GraphMindStudio
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Common.GraphMindStudio.GlobalValue;

    public static class ParameterCreate
    {
        public static void SetValueConfig(Dictionary<string, object> dataStorage, Dictionary<string, DictionaryProcessor.KeyPair> ConnectChainInfoKey, int count, List<object> listobj = null)
        {
        }

        public static void SetValueConfig(Dictionary<string, DictionaryProcessor.KeyPair> ConnectChainInfoKey, int count)
        {
        }

        private static List<object> CreateObjectList(int count, string value)
        {
            var list = new List<object>();
            for (int i = 0; i < count; i++) list.Add(value);
            return list;
        }
    }
}
