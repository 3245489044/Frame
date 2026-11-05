using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 一次 UI 声明发射的产物：<c>ui-declaration-v1</c> 规范化 JSON + 全字段指纹 + 发射期诊断。
/// <para>声明缺项 / 引用未声明区域 / 键集不闭合 ⇒ **不产出**（诊断可见化，不静默兜底）。</para>
/// </summary>
public sealed record UsharpUiDeclarationResult(
    string? Json,
    string? Fingerprint,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// 一次 UI 区域声明发射的产物：<c>ui-regions-v1</c> 兼容视图（运行期与既有门禁的读取面）+ 诊断。
/// <para>由 <see cref="UsharpUiDeclarationEmitter.Emit"/> 的产物投影派生（**非第二真源**）。</para>
/// </summary>
public sealed record UsharpUiRegionsResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary>
/// UI 声明（<c>Config/ui/ui-view.ushar</c> + <c>Config/ui/ui-loc.ushar</c>）→ 中立 JSON。
/// <para>
/// 单一真源 = 声明面：<c>[Record] view_*</c>（视图/区域规则/迁移/呈现契约）与 <c>[Record] loc_*</c>（loc 属性）。
/// <c>containers</c> **不落声明**：仍由「壳槽 + 视图区域集」派生（派生单源保持）。
/// </para>
/// <para>
/// 指纹 = 正文（<c>schema/root/views/containers/locs/defaults</c>）紧凑 JSON 的 SHA256；
/// 序列化必须使用 <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>（中文原样，禁 <c>\uXXXX</c>）。
/// </para>
/// </summary>
public static class UsharpUiDeclarationEmitter
{
    /// <summary>产物 schema 哨兵（唯一权威）。</summary>
    public const string Schema = "ui-declaration-v1";

    /// <summary>页面视图宿主名（唯一权威）。</summary>
    public const string Root = "ui-root";

    /// <summary>视图记录前缀（记录 id = <c>view_&lt;viewKey&gt;</c>）。</summary>
    public const string ViewRecordPrefix = "view_";

    /// <summary>区域记录前缀（记录 id = <c>region_&lt;kind&gt;</c>）。</summary>
    public const string RegionRecordPrefix = "region_";

    /// <summary>loc 记录前缀（记录 id = <c>loc_&lt;loc&gt;</c>）。</summary>
    public const string LocRecordPrefix = "loc_";

    /// <summary>视图表 id（<c>[Table]</c>；记录 <c>table=</c> 的唯一合法目标）。</summary>
    public const string ViewTable = "UiView";

    /// <summary>loc 表 id。</summary>
    public const string LocTable = "UiLoc";

    /// <summary>loc 坐标键空间 id（<c>[Space]</c>；与 <c>loc_*</c> 记录集必须键集相等）。</summary>
    public const string LocSpace = "UiLoc";

    /// <summary>壳槽（每视图恒有；顺序即产物顺序）。</summary>
    private static readonly string[] ShellSlots = { "region-seg", "region-head", "region-subhead" };

    /// <summary>生效默认值表（单源可见化；运行期不再 ?? 兜底）。</summary>
    private static readonly (string Path, string Value)[] Defaults =
    {
        ("views[].typeKey", "<view>"),
        ("views[].scopeKey", "task"),
        ("views[].snapshot", "task"),
        ("views[].lifecycle", "on-demand"),
        ("views[].content.scope", "task"),
        ("views[].content.fold", ""),
        ("views[].anchor.onUnknown", "default"),
        ("views[].anchor.container", "none"),
        ("views[].panel.region", "Nodes"),
        ("views[].panel.mode", "none"),
        ("views[].sidebar", "readonly-empty"),
        ("views[].composer", "none"),
        ("views[].actorLevel", "task"),
        ("views[].gatherView", "<view>"),
        ("views[].board", "none"),
        ("views[].emptyShortCircuit", "true"),
        ("views[].sectionFilter", "all"),
        ("views[].renderMode", "conversation"),
        ("views[].selectorOrder", "100"),
        ("views[].fields.*.visible", "true"),
        ("views[].regionRules.*.grouping", "none"),
        ("views[].regionRules.*.suppressHeader", "<roundVisible>"),
    };

    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions Indented = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };
    /// <summary>发射 UI 声明产物（缺项 ⇒ 不产出 + error）。</summary>
    public static UsharpUiDeclarationResult Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        var file = compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)";

        RequireTable(compilation, ViewTable, diagnostics, file);
        RequireTable(compilation, LocTable, diagnostics, file);

        var regions = ReadRegions(compilation, diagnostics);

        var views = new List<(JsonObject Doc, int Order, string View, List<(string Key, string Role)> Containers)>();
        foreach (var record in compilation.Records)
        {
            if (!record.RecordId.StartsWith(ViewRecordPrefix, StringComparison.Ordinal)) continue;
            foreach (var line in record.UnparsedLines)
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                    $"[Record] {record.RecordId}: 无法解析的字段行 '{line}'", record.SourceFile, record.LineNumber));
            }

            var built = BuildView(record, regions, diagnostics);
            if (built is { } view) views.Add(view);
        }

        if (views.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                "UI 声明不可产出：未找到任何 [Record] view_*（视图不可渲染）", file, 1));
        }

        var order = views
            .OrderBy(v => v.Order)
            .ThenBy(v => v.View, StringComparer.Ordinal)
            .ToList();

        var viewsArray = new JsonArray();
        foreach (var view in order) viewsArray.Add(view.Doc);

        // 容器序 = 视图**声明序**（产物契约；视图段按 selectorOrder 排序，两者序不同）。
        var containersArray = new JsonArray();
        foreach (var view in views)
        {
            foreach (var container in view.Containers)
            {
                containersArray.Add(new JsonObject
                {
                    ["view"] = view.View,
                    ["key"] = container.Key,
                    ["owner"] = view.View,
                    ["role"] = container.Role,
                });
            }
        }

        var locsArray = BuildLocs(compilation, diagnostics);

        if (diagnostics.Any(d => d.Severity == UsharpSeverity.Error))
        {
            return new UsharpUiDeclarationResult(null, null, diagnostics);
        }

        var defaultsArray = new JsonArray();
        foreach (var (path, value) in Defaults)
        {
            defaultsArray.Add(new JsonObject { ["path"] = path, ["value"] = value });
        }

        var body = new JsonObject
        {
            ["schema"] = Schema,
            ["root"] = Root,
            ["views"] = viewsArray,
            ["containers"] = containersArray,
            ["locs"] = locsArray,
            ["defaults"] = defaultsArray,
        };

        var compactJson = body.ToJsonString(Compact);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(compactJson))).ToLowerInvariant();

        // 文档序 = schema / fingerprint / root / views / containers / locs / defaults（产物契约）。
        var document = JsonNode.Parse(compactJson)!.AsObject();
        document.Insert(1, "fingerprint", hash);

        return new UsharpUiDeclarationResult(document.ToJsonString(Indented), hash, diagnostics);
    }
    /// <summary>兼容视图 schema（<c>ui-regions-v1</c>；运行期 ViewCompiler 与既有门禁的读取面）。</summary>
    public const string RegionsSchema = "ui-regions-v1";

    /// <summary>生成标记（判别「声明 JSON」与「产物 JSON」的结构事实；SS2 门禁读此串）。</summary>
    public const string GeneratedMarker = "GENERATED from Config/ui/ui-view.ushar";

    /// <summary>
    /// 发射 <c>ui-regions-v1</c> 兼容视图（由 <see cref="Emit"/> 产物**投影**派生，非第二真源）。
    /// <para>用途：运行期 <c>ViewCompiler</c> 与既有 9 个门禁的读取面；待消费侧迁到端口后即可删除。</para>
    /// </summary>
    public static UsharpUiRegionsResult EmitRegions(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var emitted = Emit(compilation);
        if (emitted.Json is null) return new UsharpUiRegionsResult(null, emitted.Diagnostics);

        var regions = new JsonArray();
        foreach (var record in compilation.Records)
        {
            if (!record.RecordId.StartsWith(RegionRecordPrefix, StringComparison.Ordinal)) continue;
            var loc = StrOrNull(record.Body, "loc");
            regions.Add(new JsonObject
            {
                ["kind"] = Str(record.Body, "kind", record.RecordId[RegionRecordPrefix.Length..]),
                ["key"] = Str(record.Body, "key", string.Empty),
                ["role"] = Str(record.Body, "role", "region"),
                ["loc"] = loc is null ? null : JsonValue.Create(loc),
            });
        }

        // 视图按**声明序**（= 兼容视图既有顺序）；regionRules/fields/factPriority 保留声明原值。
        var views = new JsonArray();
        foreach (var record in compilation.Records)
        {
            if (!record.RecordId.StartsWith(ViewRecordPrefix, StringComparison.Ordinal)) continue;
            var body = record.Body;
            var view = Str(body, "view", record.RecordId[ViewRecordPrefix.Length..]);

            var kinds = new JsonArray();
            foreach (var kind in StrList(body, "regions")) kinds.Add(kind);

            var migrations = new JsonArray();
            if (Value(body, "stateMigration") is UsharpList migrationList)
            {
                foreach (var item in migrationList.Items)
                {
                    if (item is not UsharpRecord migration) continue;
                    migrations.Add(new JsonObject
                    {
                        ["kind"] = Str(migration, "kind", string.Empty),
                        ["key"] = Str(migration, "key", string.Empty),
                    });
                }
            }

            var content = Rec(body, "content");
            var anchor = Rec(body, "anchor");
            var panel = Rec(body, "panel");

            var viewObject = new JsonObject
            {
                ["view"] = view,
                ["viewType"] = Str(body, "viewType", string.Empty),
                ["typeKey"] = Str(body, "typeKey", view),
                ["scopeKey"] = Str(body, "scopeKey", "task"),
                ["snapshot"] = Str(body, "snapshot", "task"),
                ["regions"] = kinds,
                ["lifecycle"] = Str(body, "lifecycle", "on-demand"),
                ["stateMigration"] = migrations,
            };

            if (Rec(body, "fields") is { } fields)
            {
                var fieldsObject = new JsonObject();
                foreach (var field in fields.Fields)
                {
                    var visible = field.Value is UsharpRecord fieldBody ? Bool(fieldBody, "visible", true) : true;
                    fieldsObject[field.Key] = new JsonObject { ["visible"] = visible };
                }

                if (fieldsObject.Count > 0) viewObject["fields"] = fieldsObject;
            }

            if (Rec(body, "regionRules") is { } rules)
            {
                var rulesObject = new JsonObject();
                foreach (var rule in rules.Fields)
                {
                    var ruleBody = rule.Value as UsharpRecord;
                    rulesObject[rule.Key] = new JsonObject
                    {
                        ["grouping"] = ruleBody is null ? "none" : Str(ruleBody, "grouping", "none"),
                        ["suppressHeader"] = ruleBody is not null && Bool(ruleBody, "suppressHeader", false),
                    };
                }

                if (rulesObject.Count > 0) viewObject["regionRules"] = rulesObject;
            }

            var factPriority = StrList(body, "factPriority");
            if (factPriority.Count > 0) viewObject["factPriority"] = ToArray(factPriority);

            viewObject["content"] = new JsonObject
            {
                ["domain"] = content is null ? string.Empty : Str(content, "domain", string.Empty),
                ["scope"] = content is null ? "task" : Str(content, "scope", "task"),
                ["fold"] = content is null ? string.Empty : Str(content, "fold", string.Empty),
            };
            viewObject["anchor"] = new JsonObject
            {
                ["chain"] = ToArray(anchor is null ? Array.Empty<string>() : StrList(anchor, "chain")),
                ["onUnknown"] = anchor is null ? "default" : Str(anchor, "onUnknown", "default"),
                ["container"] = anchor is null ? "none" : Str(anchor, "container", "none"),
            };
            viewObject["panel"] = new JsonObject
            {
                ["region"] = panel is null ? "Nodes" : Str(panel, "region", "Nodes"),
                ["domain"] = panel is null ? string.Empty : Str(panel, "domain", string.Empty),
                ["mode"] = panel is null ? "none" : Str(panel, "mode", "none"),
            };
            viewObject["sidebar"] = Str(body, "sidebar", "readonly-empty");
            viewObject["composer"] = Str(body, "composer", "none");
            viewObject["actorLevel"] = Str(body, "actorLevel", "task");
            viewObject["board"] = Str(body, "board", "none");
            viewObject["emptyShortCircuit"] = Bool(body, "emptyShortCircuit", true);
            viewObject["sectionFilter"] = Str(body, "sectionFilter", "all");
            viewObject["renderMode"] = Str(body, "renderMode", "conversation");
            viewObject["gatherView"] = Str(body, "gatherView", view);
            viewObject["selectorOrder"] = Int(body, "selectorOrder", 100);
            viewObject["titleLoc"] = Str(body, "titleLoc", $"text_view_{view}_title");
            viewObject["subtitleLoc"] = Str(body, "subtitleLoc", $"text_view_{view}_subtitle");
            viewObject["labelLoc"] = Str(body, "labelLoc", $"text_view_{view}_label");

            views.Add(viewObject);
        }

        var document = new JsonObject
        {
            ["$comment"] = GeneratedMarker
                + "（唯一真源 = Config/ui/ui-view.ushar；重生成 = Generate-UiDeclaration.ps1 -Apply）",
            ["schema"] = RegionsSchema,
            ["regions"] = regions,
            ["views"] = views,
        };

        return new UsharpUiRegionsResult(document.ToJsonString(Indented), emitted.Diagnostics);
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    private static void RequireTable(
        UsharpCompilation compilation, string tableId, List<UsharpDiagnostic> diagnostics, string file)
    {
        if (compilation.Tables.Any(t => string.Equals(t.TableId, tableId, StringComparison.Ordinal))) return;
        diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
            $"UI 声明不可产出：未找到 [Table] {tableId}（[Record] table= 的唯一合法目标）", file, 1));
    }

    /// <summary>区域定义（kind → key/role/loc）；<c>[Record] region_*</c> 单源。</summary>
    private static Dictionary<string, (string Kind, string Key, string Role, string? Loc)> ReadRegions(
        UsharpCompilation compilation, List<UsharpDiagnostic> diagnostics)
    {
        var map = new Dictionary<string, (string Kind, string Key, string Role, string? Loc)>(StringComparer.Ordinal);
        foreach (var record in compilation.Records)
        {
            if (!record.RecordId.StartsWith(RegionRecordPrefix, StringComparison.Ordinal)) continue;
            var kind = Str(record.Body, "kind", record.RecordId[RegionRecordPrefix.Length..]);
            var key = Str(record.Body, "key", string.Empty);
            if (kind.Length == 0 || key.Length == 0)
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                    $"[Record] {record.RecordId}: 区域声明缺 kind/key", record.SourceFile, record.LineNumber));
                continue;
            }

            map[kind] = (kind, key, Str(record.Body, "role", "region"), StrOrNull(record.Body, "loc"));
        }

        if (map.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                $"UI 声明不可产出：未找到任何 [Record] {RegionRecordPrefix}*（区域声明单源不可为空）",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 1));
        }

        return map;
    }
    /// <summary>单视图装配（字段序 = 产物契约序；未知区域 / 缺 content.domain ⇒ 不产出）。</summary>
    private static (JsonObject Doc, int Order, string View, List<(string Key, string Role)> Containers)? BuildView(
        UsharpRecordDecl record,
        IReadOnlyDictionary<string, (string Kind, string Key, string Role, string? Loc)> regions,
        List<UsharpDiagnostic> diagnostics)
    {
        var body = record.Body;
        var suffix = record.RecordId[ViewRecordPrefix.Length..];
        var view = Str(body, "view", suffix);
        if (view.Length == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                $"[Record] {record.RecordId}: 缺 view", record.SourceFile, record.LineNumber));
            return null;
        }

        // E3：唯一键名 = stateMigration；复数键一律拒绝（禁双写迁移表）。
        if (body.TryGet("stateMigrations", out _))
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                $"视图 '{view}' 使用复数键 stateMigrations（唯一键名 = stateMigration）",
                record.SourceFile, record.LineNumber));
            return null;
        }

        var kinds = StrList(body, "regions");
        if (kinds.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                $"视图 '{view}' 区域集为空", record.SourceFile, record.LineNumber));
            return null;
        }

        var regionDocs = new JsonArray();
        var containers = new List<(string Key, string Role)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in ShellSlots)
        {
            if (seen.Add(slot)) containers.Add((slot, "region"));
        }

        foreach (var kind in kinds)
        {
            if (!regions.TryGetValue(kind, out var definition))
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                    $"视图 '{view}' 引用未声明区域：{kind}", record.SourceFile, record.LineNumber));
                return null;
            }

            regionDocs.Add(new JsonObject
            {
                ["kind"] = definition.Kind,
                ["key"] = definition.Key,
                ["role"] = definition.Role,
                ["loc"] = definition.Loc is null ? null : JsonValue.Create(definition.Loc),
            });
            if (seen.Add(definition.Key)) containers.Add((definition.Key, definition.Role));
        }

        var migrationDocs = new JsonArray();
        if (Value(body, "stateMigration") is UsharpList migrations)
        {
            foreach (var item in migrations.Items)
            {
                if (item is not UsharpRecord migration) continue;
                var migrationKind = Str(migration, "kind", string.Empty);
                var migrationKey = Str(migration, "key", string.Empty);
                if (migrationKind.Length == 0 || migrationKey.Length == 0)
                {
                    diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                        $"视图 '{view}' stateMigration 缺 kind/key", record.SourceFile, record.LineNumber));
                    continue;
                }

                if (!seen.Contains(migrationKey))
                {
                    diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                        $"视图 '{view}' 状态迁移指向不存在容器：{migrationKey}",
                        record.SourceFile, record.LineNumber));
                    continue;
                }

                migrationDocs.Add(new JsonObject { ["kind"] = migrationKind, ["key"] = migrationKey });
            }
        }
        var content = Rec(body, "content");
        var domain = content is null ? string.Empty : Str(content, "domain", string.Empty);
        if (domain.Length == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                $"视图 '{view}' 缺 content.domain（禁空内容身份）", record.SourceFile, record.LineNumber));
            return null;
        }

        var contentScope = content is null ? "task" : Str(content, "scope", "task");
        var contentFold = content is null ? string.Empty : Str(content, "fold", string.Empty);

        var anchor = Rec(body, "anchor");
        var anchorChain = anchor is null ? new List<string>() : StrList(anchor, "chain").ToList();
        if (anchorChain.Count == 0) anchorChain.AddRange(new[] { "incoming", "sticky", "declared-default" });
        var anchorOnUnknown = anchor is null ? "default" : Str(anchor, "onUnknown", "default");
        var anchorContainer = anchor is null ? "none" : Str(anchor, "container", "none");

        var panel = Rec(body, "panel");
        var panelRegion = panel is null ? "Nodes" : Str(panel, "region", "Nodes");
        var panelDomain = panel is null ? string.Empty : Str(panel, "domain", string.Empty);
        var panelMode = panel is null ? "none" : Str(panel, "mode", "none");

        var fieldsRecord = Rec(body, "fields");
        var fieldsObject = new JsonObject();
        var roundVisible = true;
        if (fieldsRecord is not null)
        {
            foreach (var field in fieldsRecord.Fields)
            {
                var visible = true;
                if (field.Value is UsharpRecord fieldBody) visible = Bool(fieldBody, "visible", true);
                fieldsObject[field.Key] = new JsonObject { ["visible"] = visible };
            }
        }

        if (fieldsObject.TryGetPropertyValue("round", out var roundNode)
            && roundNode is JsonObject roundObject
            && roundObject.TryGetPropertyValue("visible", out var visibleNode))
        {
            roundVisible = visibleNode!.GetValue<bool>();
        }

        var rulesRecord = Rec(body, "regionRules");
        var rulesObject = new JsonObject();
        if (rulesRecord is not null)
        {
            foreach (var rule in rulesRecord.Fields)
            {
                var grouping = "none";
                var suppress = !roundVisible;
                var allow = new List<string>();
                if (rule.Value is UsharpRecord ruleBody)
                {
                    if (string.Equals(Str(ruleBody, "grouping", "none"), "by-round", StringComparison.Ordinal))
                        grouping = "by-round";
                    if (ruleBody.TryGet("suppressHeader", out _)) suppress = !Bool(ruleBody, "suppressHeader", true);
                    allow = StrList(ruleBody, "fields").ToList();
                }

                var allowArray = new JsonArray();
                foreach (var item in allow) allowArray.Add(item);
                rulesObject[rule.Key] = new JsonObject
                {
                    ["grouping"] = grouping,
                    ["suppressHeader"] = suppress,
                    ["fields"] = allowArray,
                };
            }
        }

        var chainArray = new JsonArray();
        foreach (var item in anchorChain) chainArray.Add(item);

        var factArray = new JsonArray();
        foreach (var item in StrList(body, "factPriority")) factArray.Add(item);

        var selectorOrder = Int(body, "selectorOrder", 100);
        var lifecycle = string.Equals(Str(body, "lifecycle", "on-demand"), "persistent", StringComparison.Ordinal)
            ? "persistent"
            : "on-demand";

        var document = new JsonObject
        {
            ["view"] = view,
            ["viewType"] = Str(body, "viewType", string.Empty),
            ["typeKey"] = Str(body, "typeKey", view),
            ["scopeKey"] = Str(body, "scopeKey", "task"),
            ["snapshot"] = Str(body, "snapshot", "task"),
            ["lifecycle"] = lifecycle,
            ["regions"] = regionDocs,
            ["stateMigration"] = migrationDocs,
            ["content"] = new JsonObject
            {
                ["domain"] = domain,
                ["scope"] = contentScope,
                ["fold"] = contentFold,
            },
            ["anchor"] = new JsonObject
            {
                ["chain"] = chainArray,
                ["onUnknown"] = anchorOnUnknown,
                ["container"] = anchorContainer,
            },
            ["panel"] = new JsonObject
            {
                ["region"] = panelRegion,
                ["domain"] = panelDomain,
                ["mode"] = panelMode,
            },
            ["regionRules"] = rulesObject,
            ["fields"] = fieldsObject,
            ["factPriority"] = factArray,
            ["sidebar"] = Str(body, "sidebar", "readonly-empty"),
            ["composer"] = Str(body, "composer", "none"),
            ["actorLevel"] = Str(body, "actorLevel", "task"),
            ["titleLoc"] = Str(body, "titleLoc", $"text_view_{view}_title"),
            ["subtitleLoc"] = Str(body, "subtitleLoc", $"text_view_{view}_subtitle"),
            ["gatherView"] = Str(body, "gatherView", view),
            ["board"] = Str(body, "board", "none"),
            ["emptyShortCircuit"] = Bool(body, "emptyShortCircuit", true),
            ["sectionFilter"] = Str(body, "sectionFilter", "all"),
            ["renderMode"] = Str(body, "renderMode", "conversation"),
            ["selectorOrder"] = selectorOrder,
            ["labelLoc"] = Str(body, "labelLoc", $"text_view_{view}_label"),
            ["roundVisible"] = roundVisible,
        };

        return (document, selectorOrder, view, containers);
    }
    /// <summary>loc 段（声明序 = <c>[Record] loc_*</c> 序）；键集必须与 <c>[Space] UiLoc</c> 相等。</summary>
    private static JsonArray BuildLocs(UsharpCompilation compilation, List<UsharpDiagnostic> diagnostics)
    {
        var array = new JsonArray();
        var recordLocs = new List<string>();

        foreach (var record in compilation.Records)
        {
            if (!record.RecordId.StartsWith(LocRecordPrefix, StringComparison.Ordinal)) continue;
            var loc = record.RecordId[LocRecordPrefix.Length..];
            recordLocs.Add(loc);

            foreach (var line in record.UnparsedLines)
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                    $"[Record] {record.RecordId}: 无法解析的字段行 '{line}'", record.SourceFile, record.LineNumber));
            }

            JsonObject? matrix = null;
            if (Rec(record.Body, "matrix") is { } matrixBody)
            {
                matrix = new JsonObject();
                foreach (var pair in matrixBody.Fields)
                {
                    if (pair.Value is UsharpScalar scalar && scalar.Kind == UsharpScalarKind.String)
                        matrix[pair.Key] = scalar.Text;
                }
            }

            var value = StrOrNull(record.Body, "value");
            var onMiss = StrOrNull(record.Body, "onMiss");
            array.Add(new JsonObject
            {
                ["loc"] = loc,
                ["kind"] = Str(record.Body, "kind", "text"),
                ["value"] = value is null ? null : JsonValue.Create(value),
                ["onMiss"] = onMiss is null ? null : JsonValue.Create(onMiss),
                ["matrix"] = matrix,
            });
        }

        var file = compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)";
        if (!compilation.Spaces.TryGet(LocSpace, out var space))
        {
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationNotProducible,
                $"UI 声明不可产出：未找到 [Space] {LocSpace}（loc 坐标集单源）", file, 1));
            return array;
        }

        var declared = space.Axes.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        var records = recordLocs.ToHashSet(StringComparer.Ordinal);
        foreach (var loc in recordLocs)
        {
            if (!declared.Contains(loc))
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                    $"[Record] {LocRecordPrefix}{loc}: 坐标不在 [Space] {LocSpace} 声明内", file, 1));
            }
        }

        foreach (var axis in declared)
        {
            if (!records.Contains(axis))
            {
                diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.UiDeclarationShapeInvalid,
                    $"[Space] {LocSpace} 坐标 '{axis}' 缺属性声明（[Record] {LocRecordPrefix}{axis}）", file, 1));
            }
        }

        return array;
    }

    private static UsharpValue? Value(UsharpRecord record, string name)
        => record.TryGet(name, out var value) ? value : null;

    private static string Str(UsharpRecord record, string name, string fallback)
        => record.TryGet(name, out var value) && value is UsharpScalar { Kind: not UsharpScalarKind.Null } scalar
            ? scalar.Text
            : fallback;

    private static string? StrOrNull(UsharpRecord record, string name)
        => record.TryGet(name, out var value) && value is UsharpScalar { Kind: not UsharpScalarKind.Null } scalar
            ? scalar.Text
            : null;

    private static bool Bool(UsharpRecord record, string name, bool fallback)
        => record.TryGet(name, out var value) && value is UsharpScalar { Kind: UsharpScalarKind.Bool } scalar
            ? string.Equals(scalar.Text, "true", StringComparison.OrdinalIgnoreCase)
            : fallback;

    private static int Int(UsharpRecord record, string name, int fallback)
        => record.TryGet(name, out var value)
           && value is UsharpScalar { Kind: UsharpScalarKind.Int } scalar
           && int.TryParse(scalar.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static IReadOnlyList<string> StrList(UsharpRecord record, string name)
    {
        var list = new List<string>();
        if (record.TryGet(name, out var value) && value is UsharpList items)
        {
            foreach (var item in items.Items)
            {
                if (item is UsharpScalar { Kind: UsharpScalarKind.String } scalar && scalar.Text.Length > 0)
                    list.Add(scalar.Text);
            }
        }

        return list;
    }

    private static UsharpRecord? Rec(UsharpRecord record, string name)
        => record.TryGet(name, out var value) && value is UsharpRecord nested ? nested : null;
}
