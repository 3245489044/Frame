/**
 * CSG 验证模型（镜像 C# `ValidationResult` / `ValidationError` / `ValidationWarning` / `CsgErrorCode`）。
 */
/** 标准错误码（数值与 C# `CsgErrorCode` 完全一致）。 */
export enum CsgErrorCode {
  Unspecified = 0,

  DuplicateNodeId = 1001,
  DuplicateEdgeId = 1002,
  MissingSourceNode = 1003,
  MissingTargetNode = 1004,
  EmptyForm = 1005,
  IsolatedNode = 1006,
  CycleDetected = 1007,

  NodeCoverageViolation = 2001,
  EdgeCoverageViolation = 2002,
  ReachabilityViolation = 2003,
  TypeMismatch = 2004,

  UnsupportedConversion = 3001,
  AdapterNotFound = 3002,
  AdapterAlreadyRegistered = 3003,
  SchemaVersionMismatch = 3004,
  ProtocolVersionMismatch = 3005,
  InvalidInputType = 3006,
  InvalidOutputType = 3007,

  BatchStepFailed = 4001,
  InvalidUri = 4002,
  ResourceNotFound = 4003,
  Timeout = 4004,
}

/** 校验错误（镜像 C# `ValidationError`）。 */
export interface ValidationError {
  code: CsgErrorCode;
  rule: string;
  message: string;
  sourceId?: string | null;
  targetId?: string | null;
}

/** 校验警告（镜像 C# `ValidationWarning`）。 */
export interface ValidationWarning {
  rule: string;
  message: string;
  sourceId?: string | null;
  targetId?: string | null;
}

/** 校验结果聚合（镜像 C# `ValidationResult`）。 */
export class ValidationResult {
  private _isValid = true;
  readonly errors: ValidationError[] = [];
  readonly warnings: ValidationWarning[] = [];

  get isValid(): boolean {
    return this._isValid;
  }

  addError(rule: string, message: string, sourceId?: string | null, targetId?: string | null): void;
  addError(
    code: CsgErrorCode,
    rule: string,
    message: string,
    sourceId?: string | null,
    targetId?: string | null
  ): void;
  addError(
    ruleOrCode: string | CsgErrorCode,
    rule: string,
    message: string,
    sourceId?: string | null,
    targetId?: string | null
  ): void {
    if (typeof ruleOrCode === "string") {
      this._isValid = false;
      this.errors.push({ code: CsgErrorCode.Unspecified, rule: ruleOrCode, message: rule, sourceId, targetId });
      return;
    }
    this._isValid = false;
    this.errors.push({ code: ruleOrCode, rule, message, sourceId, targetId });
  }

  addWarning(rule: string, message: string, sourceId?: string | null, targetId?: string | null): void {
    this.warnings.push({ rule, message, sourceId, targetId });
  }

  merge(other: ValidationResult): void {
    if (!other.isValid) this._isValid = false;
    this.errors.push(...other.errors);
    this.warnings.push(...other.warnings);
  }

  toString(): string {
    return `Valid: ${this._isValid}, Errors: ${this.errors.length}, Warnings: ${this.warnings.length}`;
  }
}
