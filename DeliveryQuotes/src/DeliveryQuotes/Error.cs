namespace DeliveryQuotes;

// What went wrong, in a form the caller can switch on. Expected domain failures use the first
// four codes. The File* and Native* codes are operational problems found at a boundary.
public enum ErrorCode
{
    InvalidRequest,
    NotFound,
    CapacityExceeded,
    InvalidState,
    FileMissing,
    FileMalformed,
    FileAccess,
    NativeUnavailable,
    NativeFailure
}

public sealed record Error(ErrorCode Code, string Message);
