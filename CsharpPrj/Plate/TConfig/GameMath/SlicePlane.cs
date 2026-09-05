namespace TConfig.GameMath;

/// <summary>
/// 共享切片平面枚举。原定义重复存在于 GameEditor.ECS.Components 与
/// GameEditor.VirtualMatrix.Math.Rendering.SliceProjection，合并归位到 TConfig 基元层，消除双份维护。
/// </summary>
public enum SlicePlane
{
    XY,
    XZ,
    YZ
}
