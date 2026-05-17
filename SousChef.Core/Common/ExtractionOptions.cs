namespace SousChef.Core.Common;

public class ExtractionOptions
{
    public string Provider { get; set; } = "Anthropic";
    public string ModelName { get; set; } = "claude-sonnet-4-6";
    public string ApiKey { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
