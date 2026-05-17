namespace SousChef.Core.Common;

public record Error(string Code, string Message)
{
    public static Error NotFound(string message) => new("NOT_FOUND", message);
    public static Error Validation(string message) => new("VALIDATION", message);
    public static Error Internal(string message) => new("INTERNAL", message);
    public static Error Conflict(string message) => new("CONFLICT", message);
}
