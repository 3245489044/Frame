namespace Operation.DataStoreDriven.Graph;

/// <summary>图库结构化只读行（UI/聚拢建模用；与 <c>ReadDocuments</c> 文本面同源、不同出口）。</summary>
/// <param name="Key">行键（文档主键 <c>_id</c>）。</param>
/// <param name="Summary">可读摘要。</param>
/// <param name="UpdatedAt">更新时间（可空）。</param>
/// <param name="PayloadJson">原始文档 JSON。</param>
public readonly record struct GraphRow(string Key, string Summary, string UpdatedAt, string? PayloadJson);
