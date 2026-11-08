// 表面绘制兼容门面已下沉 Share.Frameworks.SurfacePaint（2026-11-09）：
// 平台无关的转发层（SkinPaintEngine/SkinMaterialCatalog/BorderPainter/...）物理位置在 Share，
// 这里以 global using 保持既有调用点不变；新代码请直接 using Share.SurfacePaint / Share.SurfacePaint.Compat。
global using Share.SurfacePaint.Compat;
global using Strategy.Refresh.Surface;
