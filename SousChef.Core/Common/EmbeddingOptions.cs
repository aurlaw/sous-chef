namespace SousChef.Core.Common;

public class EmbeddingOptions
{
    public string Provider { get; set; } = "OpenAI";
    public string ModelName { get; set; } = "text-embedding-3-small";
    public int Dimensions { get; set; } = 1536;
    public string ApiKey { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
