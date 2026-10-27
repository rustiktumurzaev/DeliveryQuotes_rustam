namespace DeliveryQuotes;

// What went wrong, in a form the caller can switch on. Expected domain failures use the first
// four codes. The File* codes are operational problems found at the input/output boundary.
public enum ErrorCode
{
    InvalidRequest,
    NotFound,
    CapacityExceeded,
    InvalidState,
    FileMissing,
    FileMalformed,
    FileAccess
}

public sealed record Error(ErrorCode Code, string Message);
