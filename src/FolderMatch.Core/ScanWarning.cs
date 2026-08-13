namespace FolderMatch.Core;

public enum ScanWarningCode
{
    AccessDenied,
    IoError,
}

public sealed record ScanWarning(ScanWarningCode Code, string Path, string Message);
