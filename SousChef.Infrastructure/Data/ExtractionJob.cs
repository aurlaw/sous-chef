namespace SousChef.Infrastructure.Data;

public enum ExtractionJobStatus
{
    Pending,
    Processing,
    Review,
    Approved,
    Rejected,
    Completed,
    Failed
}

public class ExtractionJob
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FileKey { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public ExtractionJobStatus Status { get; set; } = ExtractionJobStatus.Pending;
    public string? PdfType { get; set; }
    public string? ExtractedData { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
