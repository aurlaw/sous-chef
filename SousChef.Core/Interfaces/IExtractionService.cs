using SousChef.Core.Common;
using SousChef.Core.Models;

namespace SousChef.Core.Interfaces;

public interface IExtractionService
{
    Task<Result<ExtractionResult>> ExtractRecipesAsync(string documentText, CancellationToken ct = default);
}
