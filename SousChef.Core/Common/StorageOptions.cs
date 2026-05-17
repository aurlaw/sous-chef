namespace SousChef.Core.Common;

public class StorageOptions
{
    public string BucketName { get; set; } = "souschef";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = "http://localhost:9000";
    public string Region { get; set; } = "us-east-1";
}
