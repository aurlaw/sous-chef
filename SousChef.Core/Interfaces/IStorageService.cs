using SousChef.Core.Common;

namespace SousChef.Core.Interfaces;

public interface IStorageService
{
    Task<Result<string>> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct = default);
    Task<Result> DeleteAsync(string key, CancellationToken ct = default);
    Task<Result<bool>> ExistsAsync(string key, CancellationToken ct = default);
}
