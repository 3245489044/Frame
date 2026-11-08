#nullable enable

using System;
using System.Text.Json;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 引擎光场快照 JSON → <see cref="SurfaceLightFrame"/>（跨 ALC 的唯一数据面）。
/// <para>schema 由 <c>GameEditor.VirtualMatrix.Lighting.SurfaceLightFieldJson</c> 产出：
/// <c>v,sig,world,config,epoch,cov[4],grid[2],sun[6],tint,cells[cols*rows*6],diag</c>。</para>
/// <para>解析失败/引擎明示不可用 ⇒ null（载体保持声明值，不回落时间表）。</para>
/// </summary>
public static class SurfaceLightFrameCodec
{
    /// <summary>解析一帧光场；不可用或格式不符返回 null。</summary>
    public static SurfaceLightFrame? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var root = ConversationJson.Root(json);

            if (root.TryGetProperty("diag", out var diagnostic)
                && diagnostic.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(diagnostic.GetString()))
            {
                return null;
            }

            var coverage = root.GetProperty("cov");
            var grid = root.GetProperty("grid");
            var sun = root.GetProperty("sun");
            var columns = grid[0].GetInt32();
            var rows = grid[1].GetInt32();
            if (columns <= 0 || rows <= 0) return null;

            var expected = checked(columns * rows * 6);
            var values = new float[expected];
            var index = 0;
            foreach (var cell in root.GetProperty("cells").EnumerateArray())
            {
                if (index >= expected) return null;
                values[index++] = cell.GetSingle();
            }

            if (index != expected) return null;

            var cells = new SurfaceLightCell[columns * rows];
            for (var i = 0; i < cells.Length; i++)
            {
                var offset = i * 6;
                cells[i] = new SurfaceLightCell(
                    values[offset],
                    values[offset + 1],
                    values[offset + 2],
                    Math.Clamp(values[offset + 3], 0f, 1f),
                    Math.Clamp(values[offset + 4], 0f, 1f),
                    (int)values[offset + 5]);
            }

            var coverageSpec = new SurfaceLightCoverage(
                coverage[0].GetSingle(),
                coverage[1].GetSingle(),
                coverage[2].GetSingle(),
                coverage[3].GetSingle());

            // 可选扩展：anchor[4] = 锚点 X/Y、光斑半径、判定半径（屏幕 DIP）。
            SurfaceLightAnchor? anchor = null;
            if (root.TryGetProperty("anchor", out var anchorElement)
                && anchorElement.ValueKind == JsonValueKind.Array
                && anchorElement.GetArrayLength() >= 4)
            {
                anchor = new SurfaceLightAnchor(
                    anchorElement[0].GetSingle(),
                    anchorElement[1].GetSingle(),
                    anchorElement[2].GetSingle(),
                    anchorElement[3].GetSingle());
            }

            return new SurfaceLightFrame(
                signature: root.GetProperty("sig").GetInt64(),
                worldSignature: root.GetProperty("world").GetInt64(),
                configSignature: root.GetProperty("config").GetInt64(),
                epoch: root.GetProperty("epoch").GetInt64(),
                coverage: coverageSpec,
                columns: columns,
                rows: rows,
                sun: new SurfaceLightDirection(sun[0].GetSingle(), sun[1].GetSingle(), sun[2].GetSingle()),
                sunIntensity: sun[3].GetSingle(),
                ambient: sun[4].GetSingle(),
                warmth: sun[5].GetSingle(),
                tintArgb: root.GetProperty("tint").GetInt32(),
                cells: cells)
            {
                Anchor = anchor ?? SurfaceLightAnchor.FromCoverage(coverageSpec),
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
