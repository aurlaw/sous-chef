using SousChef.Core.Common;
using SousChef.Core.Models;

namespace SousChef.Core.Interfaces;

public interface IDocumentExtractor
{
    Task<Result<DocumentExtractionResult>> ExtractTextAsync(Stream pdfContent, CancellationToken ct = default);
}
