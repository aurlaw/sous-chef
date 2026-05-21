namespace SousChef.Core.Models;

public record JobStatusChanged(
    Guid JobId,
    string Status,
    string? Stage,
    string? Message);

public record JobReadyForReview(
    Guid JobId,
    string ExtractedData);

public record JobFailed(
    Guid JobId,
    string Stage,
    string Message,
    string? Detail);
