using SousChef.Core.Common;

namespace SousChef.Core.Interfaces;

public interface IEmbeddingService
{
    Task<Result<float[]>> GenerateAsync(string text, CancellationToken ct = default);
    Task<Result<IReadOnlyList<float[]>>> GenerateBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
