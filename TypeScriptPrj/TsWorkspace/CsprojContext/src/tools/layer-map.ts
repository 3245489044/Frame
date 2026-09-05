/**
 * 自动生成 — layer-map
 *
 * 模板类型: no-args
 * 生成时间: 2026-06-23T06:14:11.139Z
 */

import { getLayers, formatLayerMap } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {

  const results = getLayers();
  return formatLayerMap(results);
}
