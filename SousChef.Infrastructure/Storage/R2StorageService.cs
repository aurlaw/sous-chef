using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SousChef.Core.Common;
using SousChef.Core.Interfaces;

namespace SousChef.Infrastructure.Storage;

public class R2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly StorageOptions _options;

    public R2StorageService(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        _s3 = new AmazonS3Client(
            _options.AccessKeyId,
            _options.SecretAccessKey,
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = _options.Region
            });
    }

    public async Task<Result<string>> UploadAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false
            };
            await _s3.PutObjectAsync(request, ct);
            return Result<string>.Success(key);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(Error.Internal($"Storage upload failed: {ex.Message}"));
        }
    }

    public async Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_options.BucketName, key, ct);
            return Result<Stream>.Success(response.ResponseStream);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result<Stream>.Failure(Error.NotFound($"File not found: {key}"));
        }
        catch (Exception ex)
        {
            return Result<Stream>.Failure(Error.Internal($"Storage download failed: {ex.Message}"));
        }
    }

    public async Task<Result> DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.DeleteObjectAsync(_options.BucketName, key, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Internal($"Storage delete failed: {ex.Message}"));
        }
    }

    public async Task<Result<bool>> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_options.BucketName, key, ct);
            return Result<bool>.Success(true);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result<bool>.Success(false);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(Error.Internal($"Storage exists check failed: {ex.Message}"));
        }
    }
}
