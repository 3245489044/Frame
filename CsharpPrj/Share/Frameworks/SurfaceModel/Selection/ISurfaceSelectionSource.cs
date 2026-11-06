#nullable enable

namespace Share.SurfaceModel.Selection;

/// <summary>选择源（装配期注入；producer 不读静态单例）。</summary>
public interface ISurfaceSelectionSource
{
    /// <summary>取指定视图 × 载体的当前选择（含指纹）。</summary>
    /// <param name="viewKey">请求视图键。</param>
    /// <param name="carrierId">载体 id。</param>
    SurfaceSelection Select(string viewKey, string carrierId);
}
