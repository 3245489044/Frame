using System.Collections.Generic;
using System.Text.Json;
using Plate.JsonMethod.Serialization;
using Plate.JsonMethod.Validation;

namespace Strategy.DecisionRules.Configurable
{
    /// <summary>
    /// 决策控制规格的 JSON 支持（复用 Plate.JsonMethod 统一 JSON 框架做反序列化/序列化/结构校验）。
    /// 规格文件以 camelCase 书写：evidence[].zone/axis/terms、layers[].name/output/absorb/rules、
    /// rules[].label/priority/cross/when、outputMap、fallback 等。
    /// 分工：本类只做【JSON ↔ Spec】的语法/结构层；【语义层校验（轴注册/标签/引用列）】唯一归
    /// <see cref="ConfigDrivenDecisionController"/>（构筑期 Validate，TH-12 单一权威），本类不重复。
    /// </summary>
    public static class DecisionControlJson
    {
        /// <summary>
        /// Spec 序列化选项：camelCase 命名（对齐既有规则文件风格）+ 大小写不敏感读 + 注释跳过 + 缩进写出。
        /// 复刻 <c>DecisionRuleJson.DeserializeOptions</c> 约定；本规格无枚举，故无需枚举字符串转换器。
        /// </summary>
        public static JsonSerializerOptions Options { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
        };

        private static JsonSerializationService Serializer => JsonSerializationService.Instance;

        /// <summary>由 JSON 反序列化为 Spec（camelCase 规格文件）。语法/结构错误抛 <see cref="JsonException"/>（经 JsonMethod 增强诊断）。</summary>
        public static DecisionControlSpec? Deserialize(string json)
            => Serializer.Deserialize<DecisionControlSpec>(json, Options);

        /// <summary>序列化为 JSON 串（camelCase + 缩进）。</summary>
        public static string Serialize(DecisionControlSpec spec)
            => Serializer.Serialize(spec, Options);

        /// <summary>
        /// 结构校验（语法 + 可反序列化），复用 JsonMethod：非空/合法 JSON → 可解析为 Spec → 空错误。
        /// 返回结构错误列表；通过返回空。语义层校验由 <see cref="ConfigDrivenDecisionController"/> 构筑期承担。
        /// </summary>
        public static IReadOnlyList<string> Validate(string json)
        {
            var errors = new List<string>();

            var syntax = new JsonValidationService().ValidateSyntax(json);
            if (!syntax.IsValid)
            {
                errors.AddRange(syntax.Errors);
                return errors;
            }

            try
            {
                var result = Serializer.Deserialize<DecisionControlSpec>(json, Options);
                if (result is null)
                    errors.Add("决策控制规格反序列化结果为 null");
            }
            catch (System.Text.Json.JsonException ex)
            {
                errors.Add($"Spec 结构不合法：{ex.Message}");
            }
            return errors;
        }

        /// <summary>读文件 → 校验 → 反序列化；文件缺失抛 <see cref="System.IO.FileNotFoundException"/>。</summary>
        public static DecisionControlSpec? LoadFromFile(string path)
        {
            var errors = Validate(System.IO.File.ReadAllText(path));
            if (errors.Count > 0)
                throw new System.InvalidOperationException(
                    $"决策控制规格文件校验失败（{path}）：{string.Join("；", errors)}");
            return Deserialize(System.IO.File.ReadAllText(path));
        }
    }
}