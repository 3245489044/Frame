/**
 * 业务逻辑层
 *
 * 子项目在此文件中实现具体的业务逻辑。
 * handler 通过 getBusinessService() 获取此服务实例。
 *
 * @example
 * ```typescript
 * // 在 handler 中使用：
 * import { getBusinessService } from "../core/registry";
 * const business = getBusinessService<typeof business>();
 * business.someMethod(param1);
 * ```
 */

export const business = {
  /**
   * 示例方法
   * 替换为你的业务方法
   */
  exampleMethod(param1: string): string {
    return `处理完成: ${param1}`;
  },

  /**
   * 在此添加更多业务方法...
   */
};
