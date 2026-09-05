namespace Common.NewtonSoftJsonFramework
{
#nullable disable
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;

    public class JsonFrameWork { }
    public static class Deserialization
    {
        public static T Deserialize<T>(string json, JsonSerializerSettings settings = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default(T);

            try
            {
                JToken jToken = ParseJTokenWithErrorHandling<T>(json);
                if (jToken == null)
                    return default(T);

                bool inputHasParams = CheckInputHasParameters(jToken);
                var result = DeserializeWithErrorHandling<T>(json, settings);
                if (result == null)
                    return default(T);

                bool outputHasParams = CheckOutputHasParameters(result);
                return result;
            }
            catch (Exception ex)
            {
                LogDeserializationError<T>("反序列化过程中发生未预期的异常", ex, json);
                return default(T);
            }
        }

        private static JToken ParseJTokenWithErrorHandling<T>(string json)
        {
            try
            {
                return JToken.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                LogDeserializationWarning<T>($"JSON格式错误 - {ex.Message} (位置: 行{ex.LineNumber}, 列{ex.LinePosition})", json);
                return null;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                LogDeserializationWarning<T>($"JSON解析异常 - {ex.Message}", json);
                return null;
            }
            catch (Exception ex)
            {
                LogDeserializationWarning<T>($"其他异常 - {ex.GetType().Name}: {ex.Message}", json);
                return null;
            }
        }

        private static T DeserializeWithErrorHandling<T>(string json, JsonSerializerSettings settings = null)
        {
            try
            {
                return settings != null
                    ? JsonConvert.DeserializeObject<T>(json, settings)
                    : JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonSerializationException ex)
            {
                LogDeserializationWarning<T>($"序列化设置导致反序列化失败 - {ex.GetType().Name}: {ex.Message}", json);
                return default(T);
            }
            catch (Exception ex)
            {
                LogDeserializationWarning<T>($"反序列化对象失败 - {ex.GetType().Name}: {ex.Message}", json);
                return default(T);
            }
        }

        private static bool CheckInputHasParameters(JToken jToken)
        {
            return jToken.Type switch
            {
                JTokenType.Object or JTokenType.Array => jToken.HasValues,
                JTokenType.Null or JTokenType.Undefined => false,
                _ => true
            };
        }

        private static bool CheckOutputHasParameters<T>(T result)
        {
            if (result == null)
                return false;

            return true;
        }

        private static void LogDeserializationError<T>(string message, Exception ex, string json)
        {


        }

        private static void LogDeserializationWarning<T>(string message, string json)
        {

        }
    }

    public static class JsonSymbolRenamer
    {
        public static string RenameDuplicateSymbols(string jsonArray, string keyName, string separator = "_")
        {
            if (string.IsNullOrWhiteSpace(jsonArray))
                return jsonArray;

            JArray array = JArray.Parse(jsonArray);
            JArray result = RenameDuplicateSymbols(array, keyName, separator);
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static JArray RenameDuplicateSymbols(JArray array, string keyName, string separator = "_")
        {
            if (array == null)
                return null;

            Dictionary<string, int> countMap = new Dictionary<string, int>();

            foreach (JObject obj in array)
            {
                string symbol = obj[keyName]?.ToString();
                if (!string.IsNullOrEmpty(symbol))
                {
                    if (countMap.ContainsKey(symbol))
                        countMap[symbol]++;
                    else
                        countMap[symbol] = 1;
                }
            }

            JArray result = new JArray();
            Dictionary<string, int> currentCount = new Dictionary<string, int>();

            foreach (JObject obj in array)
            {
                JToken token = obj[keyName];
                if (token == null)
                {
                    result.Add(obj);
                    continue;
                }

                string originalSymbol = token.ToString();
                if (string.IsNullOrEmpty(originalSymbol))
                {
                    result.Add(obj);
                    continue;
                }

                if (countMap[originalSymbol] > 1)
                {
                    if (!currentCount.ContainsKey(originalSymbol))
                    {
                        currentCount[originalSymbol] = 1;
                        result.Add(obj);
                    }
                    else
                    {
                        currentCount[originalSymbol]++;
                        string newSymbol = originalSymbol + separator + currentCount[originalSymbol];
                        JObject newObj = (JObject)obj.DeepClone();
                        newObj[keyName] = newSymbol;
                        result.Add(newObj);
                    }
                }
                else
                {
                    result.Add(obj);
                }
            }

            return result;
        }
    }
}