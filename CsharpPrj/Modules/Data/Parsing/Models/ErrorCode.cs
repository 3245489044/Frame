namespace Modules.Parsing.Models;

public enum ErrorCode
{
    None = 0,
    NotImplemented = 1,
    UnknownError = 1000,
    InvalidArgument = 1001,
    NullReference = 1002,
    Timeout = 1003,
    OperationCanceled = 1004,
    CommandEmpty = 1100,
    CommandFormatError = 1101,
    UnknownVerb = 1102,
    InvalidParameter = 1103,
    UnclosedQuote = 1104,
    AdapterNotFound = 1200,
    NoActiveAdapter = 1201,
    AdapterAlreadyExists = 1202,
    AdapterInitializationFailed = 1203,
    AccessDenied = 1300,
    PermissionDenied = 1301,
    AdapterAccessDenied = 1302,
    ExecutionFailed = 1400,
    ActionNotFound = 1401,
    TargetNotFound = 1402,
    ValidationFailed = 1403,
    StorageError = 1500,
    DataNotFound = 1501,
    DataConflict = 1502,
    SerializationError = 1503
}

public static class ErrorCodeExtensions
{
    private static readonly Dictionary<ErrorCode, string> _messages = new()
    {
        { ErrorCode.None, "Success" },
        { ErrorCode.UnknownError, "An unknown error occurred" },
        { ErrorCode.InvalidArgument, "Invalid argument" },
        { ErrorCode.NullReference, "Null reference" },
        { ErrorCode.Timeout, "Operation timed out" },
        { ErrorCode.OperationCanceled, "Operation was canceled" },
        { ErrorCode.CommandEmpty, "Command cannot be empty" },
        { ErrorCode.CommandFormatError, "Invalid command format" },
        { ErrorCode.UnknownVerb, "Unknown command verb" },
        { ErrorCode.InvalidParameter, "Invalid parameter" },
        { ErrorCode.UnclosedQuote, "Unclosed quote in command" },
        { ErrorCode.AdapterNotFound, "Adapter not found" },
        { ErrorCode.NoActiveAdapter, "No active adapter registered" },
        { ErrorCode.AdapterAlreadyExists, "Adapter already exists" },
        { ErrorCode.AdapterInitializationFailed, "Adapter initialization failed" },
        { ErrorCode.AccessDenied, "Access denied" },
        { ErrorCode.PermissionDenied, "Permission denied" },
        { ErrorCode.AdapterAccessDenied, "Adapter access denied" },
        { ErrorCode.ExecutionFailed, "Command execution failed" },
        { ErrorCode.ActionNotFound, "Action not found" },
        { ErrorCode.TargetNotFound, "Target not found" },
        { ErrorCode.ValidationFailed, "Validation failed" },
        { ErrorCode.StorageError, "Storage error" },
        { ErrorCode.DataNotFound, "Data not found" },
        { ErrorCode.DataConflict, "Data conflict" },
        { ErrorCode.SerializationError, "Serialization error" }
    };

    public static string GetMessage(this ErrorCode code)
        => _messages.TryGetValue(code, out var message) ? message : "Unknown error";

    public static bool IsSuccess(this ErrorCode code) => code == ErrorCode.None;
    public static bool IsError(this ErrorCode code) => code != ErrorCode.None;
    public static bool IsFatal(this ErrorCode code) => (int)code >= 1500;
}
