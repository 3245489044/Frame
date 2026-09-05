/**
 * CSG 协议版本（镜像 C# `CsgProtocol`）— 跨语言兼容性校验的统一入口。
 */
export class CsgProtocol {
  /** 当前协议版本（语义化版本号，与 C# `CsgProtocol.CurrentVersion` 对齐）。 */
  static readonly CurrentVersion = "1.0.0";

  /** 最低支持版本。 */
  static readonly MinimumSupportedVersion = "1.0.0";

  static readonly Name = "CSG Unified Protocol";

  static readonly Description = "Configuration-StateMachine-Graph 统一中间表示协议";

  private constructor() {}

  /** 将语义化版本解析为 [major, minor, patch]；非法则抛错。 */
  static parseVersion(version: string): [number, number, number] {
    const parts = version.split(".").map((p) => Number(p));
    if (parts.length !== 3 || parts.some((p) => !Number.isInteger(p))) {
      throw new Error(`CSG 协议版本格式非法: ${version}（应为 x.y.z）`);
    }
    return [parts[0], parts[1], parts[2]];
  }

  private static gte(a: [number, number, number], b: [number, number, number]): boolean {
    return a[0] !== b[0] ? a[0] > b[0] : a[1] !== b[1] ? a[1] > b[1] : a[2] >= b[2];
  }

  private static lte(a: [number, number, number], b: [number, number, number]): boolean {
    return a[0] !== b[0] ? a[0] < b[0] : a[1] !== b[1] ? a[1] < b[1] : a[2] <= b[2];
  }

  /** 版本是否在当前支持区间 [MinimumSupportedVersion, CurrentVersion] 内。 */
  static isCompatible(version: string): boolean {
    try {
      const v = CsgProtocol.parseVersion(version);
      const min = CsgProtocol.parseVersion(CsgProtocol.MinimumSupportedVersion);
      const cur = CsgProtocol.parseVersion(CsgProtocol.CurrentVersion);
      return CsgProtocol.gte(v, min) && CsgProtocol.lte(v, cur);
    } catch {
      return false;
    }
  }

  /** 版本不兼容时抛错（镜像 C# `EnsureCompatible`）。 */
  static ensureCompatible(version: string, source = ""): void {
    if (!CsgProtocol.isCompatible(version)) {
      const prefix = source ? `[${source}] ` : "";
      throw new Error(
        `${prefix}协议版本不兼容: 当前 ${CsgProtocol.CurrentVersion}, 最低支持 ${CsgProtocol.MinimumSupportedVersion}, 传入 ${version}`
      );
    }
  }

  /** 协议信息摘要。 */
  static getInfo(): string {
    return `${CsgProtocol.Name} v${CsgProtocol.CurrentVersion} (min: v${CsgProtocol.MinimumSupportedVersion}) - ${CsgProtocol.Description}`;
  }
}
