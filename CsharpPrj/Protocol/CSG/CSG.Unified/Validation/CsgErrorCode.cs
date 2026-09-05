namespace CSG.Unified;

public enum CsgErrorCode
{
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
